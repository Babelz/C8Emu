using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C8Emu
{
    /// <summary>
    /// Chip 8 CPU.
    /// </summary>
    internal sealed class Cpu
    {
        #region Constants
        private const int GFX_WIDTH = 64;
        private const int GFX_HEIGHT = 32;
        #endregion

        #region Fields
        private readonly Random random = new Random();

        private Stopwatch clock;

        // Chip 8 has HEX based keypad (0x0 - 0xF).
        private readonly byte[] key = new byte[16];

        private readonly ushort[] stack = new ushort[16];

        // The chip has 4k memory in total.
        private readonly byte[] memory = new byte[4096];

        // The chip has 15 8-bit general purpose registers named V0, V1 up to VE.
        // The 16th register is used for the 'carry flag'. 
        private readonly byte[] V = new byte[16];

        // Stack pointer.
        private ushort sp;

        // There is and index register 'ir' and a program counter (pc) which can have a value from
        // 0x000 to 0xFFF.
        private ushort ir;
        private ushort pc;

        // The system memory map:
        // 0x000 - 0x1FF = Chip 8 interpreter (contains font set in emu)
        // 0x050 - 0x0A0 = Used for the built in 4x5 pixel font set (0-F)
        // 0x200 - 0xFFF = Program ROM and work RAM

        // The chip 8 has none, but there are two timer registers
        // that count at 60Hz. When set above zero, they will count down to zero.
        // The system's buzzer sounds whenever the sound timer reaches zero.
        private byte delayTimer;
        private byte soundTimer;

        // Current opcode.
        private ushort opcode;

        private bool initialized;
        private int programLength;

        public int ticksPerCycle;
        public readonly short[][] gfx;
        #endregion

        #region Properties
        public short[][] Gfx
        {
            get
            {
                return gfx;
            }
        }
        public int TicksPerCycle
        {
            get
            {
                return ticksPerCycle;
            }
            set
            {
                ticksPerCycle = value;
            }
        }
        #endregion

        public Cpu()
        {
            gfx = new short[GFX_HEIGHT][];
            
            for (int i = 0; i < GFX_HEIGHT; i++)
            {
                gfx[i] = new short[GFX_WIDTH];
            }

            ticksPerCycle = 1;
        }

        /// <summary>
        /// Durning this step, the system will fetch one opcode from the memory at the location specified 
        /// by the program counter (pc). In our chip 8 emulator, data is stored in an array in wich each 
        /// address contains one byte. As one opcode is 2 bytes long, we will need to fetch two successive 
        /// bytes and merge the to get the actual opcode.
        /// </summary>
        private void FetchOpcode()
        {
            opcode = (ushort)(memory[pc] << 8 | memory[pc + 1]);
        }

        private void ExecuteOpcode()
        {
            switch (opcode & 0xF000)
            {
                case 0x0000:
                    switch (opcode & 0x000F)
                    {
                        // Clears the screen, 00E0.
                        case 0x0000:
                            // Sets all pixels to, 0x0.
                            for (int i = 0; i < gfx.Length; i++)
                            {
                                for (int j = 0; j < gfx[i].Length; j++)
                                {
                                    gfx[i][j] = 0x0;
                                }
                            }

                            pc += 2;
                            break;

                        // Returns from a subroutine, 00EE.
                        case 0x000E:
                            // 16 levels of stack, decrease stack pointer to prevent overwrite.
                            sp--;

                            // Put the stored return address from stack back into the program counter.
                            pc = stack[sp];

                            pc += 2;
                            break;

                        default:
                            break;
                    }
                    break;

                // Jumps to address NNN, 0x1NNN.
                case 0x1000:
                    pc = (ushort)(opcode & 0x0FFF);
                    break;

                // Calls subroutine at NNN, 0x2NNN.
                case 0x2000:
                    // Save pc to stack.
                    stack[sp] = pc;

                    sp++;

                    pc = (ushort)(opcode & 0x0FFF);
                    break;

                // Skips the next instruction if VX equals NN. 0x3XNN.
                case 0x3000:
                    // Gets register value from (opcode AND 0x0F00 shift to right by 8 bits).
                    if (V[(opcode & 0x0F00) >> 8] == (opcode & 0x00FF))
                    {
                        // Skipping instruction.
                        pc += 4;
                    }
                    else
                    {
                        pc += 2;
                    }
                    break;

                // Skips the next instruction if VX dosen't equal NN. 0x4XNN.
                case 0x4000:
                    if (V[(opcode & 0x0F00) >> 8] != (opcode & 0x00FF))
                    {
                        // Skipping instruction.
                        pc += 4;
                    }
                    else
                    {
                        pc += 2;
                    }
                    break;

                // Skips the next instruction if VX equals VY.
                case 0x5000:
                    if (V[(opcode & 0x0F00) >> 8] == V[(opcode & 0x00F0) >> 4])
                    {
                        // Skipping instruction.
                        pc += 4;
                    }
                    else
                    {
                        pc += 2;
                    }
                    break;

                // Sets VX to NN. 0x6XNN.
                case 0x6000:
                    V[(opcode & 0x0F00) >> 8] = (byte)(opcode & 0x00FF);

                    pc += 2;
                    break;

                // Adds NN to VX. 0x7XNN.
                case 0x7000:
                    V[(opcode & 0x0F00) >> 8] += (byte)(opcode & 0x00FF);

                    pc += 2;
                    break;

                case 0x8000:
                    switch (opcode & 0x000F)
                    {
                        // 0x8XY0: Sets VX to the value of VY
                        case 0x0000:
                            V[(opcode & 0x0F00) >> 8] = V[(opcode & 0x00F0) >> 4];

                            pc += 2;
                            break;

                        // 0x8XY1: Sets VX to "VX OR VY"
                        case 0x0001:
                            V[(opcode & 0x0F00) >> 8] |= V[(opcode & 0x00F0) >> 4];

                            pc += 2;
                            break;

                        // 0x8XY2: Sets VX to "VX AND VY"
                        case 0x0002:
                            V[(opcode & 0x0F00) >> 8] &= V[(opcode & 0x00F0) >> 4];

                            pc += 2;
                            break;

                        // 0x8XY3: Sets VX to "VX XOR VY"
                        case 0x0003:
                            V[(opcode & 0x0F00) >> 8] ^= V[(opcode & 0x00F0) >> 4];

                            pc += 2;
                            break;

                        // 0x8XY4: Adds VY to VX. VF is set to 1 when there's a carry, and to 0 when there isn't
                        case 0x0004:
                            if (V[(opcode & 0x00F0) >> 4] > (0xFF - V[(opcode & 0x0F00) >> 8]))
                            {
                                V[0xF] = 1; //carry
                            }
                            else
                            {
                                V[0xF] = 0;
                            }

                            V[(opcode & 0x0F00) >> 8] += V[(opcode & 0x00F0) >> 4];

                            pc += 2;
                            break;

                        // 0x8XY5: VY is subtracted from VX. VF is set to 0 when there's a borrow, and 1 when there isn't
                        case 0x0005:
                            if (V[(opcode & 0x00F0) >> 4] > V[(opcode & 0x0F00) >> 8])
                            {
                                V[0xF] = 0; // there is a borrow
                            }
                            else
                            {
                                V[0xF] = 1;
                                V[(opcode & 0x0F00) >> 8] -= V[(opcode & 0x00F0) >> 4];
                            }

                            pc += 2;
                            break;

                        // 0x8XY6: Shifts VX right by one. VF is set to the value of the least significant bit of VX before the shift
                        case 0x0006:
                            V[0xF] = (byte)(V[(opcode & 0x0F00) >> 8] & 0x1);

                            V[(opcode & 0x0F00) >> 8] >>= 1;

                            pc += 2;
                            break;

                        // 0x8XY7: Sets VX to VY minus VX. VF is set to 0 when there's a borrow, and 1 when there isn't
                        case 0x0007:
                            if (V[(opcode & 0x0F00) >> 8] > V[(opcode & 0x00F0) >> 4])
                            {
                                V[0xF] = 0;
                            }
                            else
                            {
                                V[0xF] = 1;
                            }

                            V[(opcode & 0x0F00) >> 8] = (byte)(V[(opcode & 0x00F0) >> 4] - V[(opcode & 0x0F00) >> 8]);

                            pc += 2;
                            break;

                        // 0x8XYE: Shifts VX left by one. VF is set to the value of the most significant bit of VX before the shift
                        case 0x000E:
                            V[0xF] = (byte)(V[(opcode & 0x0F00) >> 8] >> 7);

                            V[(opcode & 0x0F00) >> 8] <<= 1;

                            pc += 2;
                            break;

                        default:
                            break;//throw new InvalidOperationException("Unsupported opcode");
                    }
                    break;

                case 0x9000: // 0x9XY0: Skips the next instruction if VX doesn't equal VY
                    if (V[(opcode & 0x0F00) >> 8] != V[(opcode & 0x00F0) >> 4])
                    {
                        pc += 4;
                    }
                    else
                    {
                        pc += 2;
                    }
                    break;

                case 0xA000: // ANNN: Sets I to the address NNN
                    ir = (ushort)(opcode & 0x0FFF);

                    pc += 2;
                    break;

                case 0xB000: // BNNN: Jumps to the address NNN plus V0
                    pc = (ushort)((opcode & 0x0FFF) + V[0]);
                    break;

                case 0xC000: // CXNN: Sets VX to a random number and NN
                    V[(opcode & 0x0F00) >> 8] = (byte)((random.Next(0, 255)) & (opcode & 0x00FF));

                    pc += 2;
                    break;

                case 0xD000: 
                    {
                        short x = V[(opcode & 0x0F00) >> 8];
                        short y = V[(opcode & 0x00F0) >> 4];
                        int height = (opcode & 0x000F);
                        byte pixel;

                        // TODO: some games make this possible, dont know why.
                        if (x >= GFX_WIDTH || y >= GFX_HEIGHT)
                        {
                            pc += 2;

                            return;
                        }

                        V[0xF] = 0;

                        for (int yline = 0; yline < height; yline++)
                        {
                            pixel = memory[ir + yline];

                            for (int xline = 0; xline < 8; xline++)
                            {
                                if ((pixel & (0x80 >> xline)) != 0)
                                {
                                    if (gfx[y + yline][x + xline] == 1)
                                    {
                                        V[0xF] = 1;
                                    }

                                    gfx[y + yline][x + xline] ^= 1;
                                }
                            }
                        }

                        pc += 2;
                    }
                    break;

                case 0xE000:
                    switch (opcode & 0x00FF)
                    {
                        // EX9E: Skips the next instruction if the key stored in VX is pressed
                        case 0x009E: 
                            if (key[V[(opcode & 0x0F00) >> 8]] != 0)
                            {
                                pc += 4;
                            }
                            else
                            {
                                pc += 2;
                            }
                            break;

                        // EXA1: Skips the next instruction if the key stored in VX isn't pressed
                        case 0x00A1: 
                            if (key[V[(opcode & 0x0F00) >> 8]] == 0)
                            {
                                pc += 4;
                            }
                            else
                            {
                                pc += 2;
                            }
                            break;

                        default:
                            return;
                    }
                    break;

                case 0xF000:
                    switch (opcode & 0x00FF)
                    {
                        // FX07: Sets VX to the value of the delay timer
                        case 0x0007: 
                            V[(opcode & 0x0F00) >> 8] = delayTimer;
                            pc += 2;
                            break;

                        // FX0A: A key press is awaited, and then stored in VX	
                        case 0x000A: 	
                            {
                                bool keyPress = false;

                                for (int i = 0; i < 16; ++i)
                                {
                                    if (key[i] != 0)
                                    {
                                        V[(opcode & 0x0F00) >> 8] = (byte)i;
                                        keyPress = true;
                                    }
                                }

                                // If we didn't received a keypress, skip this cycle and try again.
                                if (!keyPress)
                                    return;

                                pc += 2;
                            }
                            break;

                        // FX15: Sets the delay timer to VX
                        case 0x0015: 
                            delayTimer = V[(opcode & 0x0F00) >> 8];
                            pc += 2;
                            break;

                        // FX18: Sets the sound timer to VX
                        case 0x0018: 
                            soundTimer = V[(opcode & 0x0F00) >> 8];
                            pc += 2;
                            break;

                        // FX1E: Adds VX to I
                        case 0x001E: 
                            if (ir + V[(opcode & 0x0F00) >> 8] > 0xFFF)
                            {
                                // VF is set to 1 when range overflow (I+VX>0xFFF), and 0 when there isn't.
                                V[0xF] = 1;
                            }
                            else
                            {
                                V[0xF] = 0;
                            }

                            ir += (ushort)V[(opcode & 0x0F00) >> 8];
                            pc += 2;
                            break;

                        // FX29: Sets I to the location of the sprite for the character in VX. Characters 0-F (in hexadecimal) are represented by a 4x5 font
                        case 0x0029: 
                            ir = (ushort)(V[(opcode & 0x0F00) >> 8] * 0x5);
                            pc += 2;
                            break;

                        // FX33: Stores the Binary-coded decimal representation of VX at the addresses I, I plus 1, and I plus 2
                        case 0x0033: 
                            memory[ir] = (byte)(V[(opcode & 0x0F00) >> 8] / 100);
                            memory[ir + 1] = (byte)((V[(opcode & 0x0F00) >> 8] / 10) % 10);
                            memory[ir + 2] = (byte)((V[(opcode & 0x0F00) >> 8] % 100) % 10);
                            pc += 2;
                            break;

                        // FX55: Stores V0 to VX in memory starting at address I	
                        case 0x0055: 				
                            for (int i = 0; i <= ((opcode & 0x0F00) >> 8); ++i)
                            {
                                memory[ir + i] = V[i];
                            }

                            // On the original interpreter, when the operation is done, I = I + X + 1.
                            ir += (ushort)(((opcode & 0x0F00) >> 8) + 1);
                            pc += 2;
                            break;

                        // FX65: Fills V0 to VX with values from memory starting at address I		
                        case 0x0065:
                            for (int i = 0; i <= ((opcode & 0x0F00) >> 8); ++i)
                            {
                                V[i] = memory[ir + i];
                            }

                            // On the original interpreter, when the operation is done, I = I + X + 1.
                            ir += (ushort)(((opcode & 0x0F00) >> 8) + 1);
                            pc += 2;
                            break;

                        default:
                            break;
                    }
                    break;

                default:
                    break;
            }
        }

        private void UpdateTimers()
        {
            if (delayTimer > 0)
            {
                delayTimer--;
            }

            if (soundTimer > 0)
            {
                if (soundTimer == 1)
                {
                    Console.WriteLine("BEEP");
                }

                soundTimer--;
            }
        }

        public void LoadGame(string fullname)
        {
            if (!initialized)
            {
                return;
            }

            byte[] bytes = File.ReadAllBytes(fullname);

            for (int i = 0; i < bytes.Length; i++)
            {
                memory[i + 512] = bytes[i];
            }

            programLength = bytes.Length;
        }

        public void Initialize()
        {
            if (initialized)
            {   
                return;
            }

            // Program counter starts at 0x200 (512).
            pc = 0x200;

            // Reset current opcode.
            opcode = 0;

            // Reset index register.
            ir = 0;

            // Reset stack pointer.
            sp = 0;

            programLength = 0;

            // Load fontset.
            //for (int i = 0; i < 80; i++)
            //{
            //}

            // Reset timers.
            delayTimer = 0;
            soundTimer = 0;

            clock = new Stopwatch();
            clock.Start();

            initialized = true;
        }
        public void Restart()
        {
            pc = 0x200;
            
            opcode = 0;
            
            ir = 0;

            sp = 0;

            delayTimer = 0;
            soundTimer = 0;
        }

        public void RunCycles()
        {
            for (int i = 0; i < ticksPerCycle; i++)
            {
                // Execute 60 opcodes per second since chip 8 is clocked at 60Hz.
                if (clock.Elapsed.Milliseconds >= 16.6)
                {
                    // Fetch Opcode.
                    FetchOpcode();

                    // Execute Opcode.
                    ExecuteOpcode();

                    // Update timers.
                    UpdateTimers();

                    //clock.Restart();
                }
            }
        }
    }
}
