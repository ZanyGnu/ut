//
// MethodBodyReader.cs
//
// Author:
//   Jb Evain (jbevain@novell.com)
//
// (C) 2009 - 2010 Novell, Inc. (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace Mono.Reflection
{

    class MethodBodyReader
    {

        static readonly OpCode[] one_byte_opcodes;
        static readonly OpCode[] two_bytes_opcodes;

        static MethodBodyReader()
        {
            one_byte_opcodes = new OpCode[0xe1];
            two_bytes_opcodes = new OpCode[0x1f];

            var fields = typeof(OpCodes).GetFields(
                BindingFlags.Public | BindingFlags.Static);

            foreach (var field in fields)
            {
                var opcode = (OpCode)field.GetValue(null);
                if (opcode.OpCodeType == OpCodeType.Nternal)
                    continue;

                if (opcode.Size == 1)
                    one_byte_opcodes[opcode.Value] = opcode;
                else
                    two_bytes_opcodes[opcode.Value & 0xff] = opcode;
            }
        }

        readonly MethodBase method;
        readonly MethodBody body;
        readonly Module module;
        readonly Type[] type_arguments;
        readonly Type[] method_arguments;
        readonly ByteBuffer il;
        readonly ParameterInfo[] parameters;
        readonly IList<LocalVariableInfo> locals;
        readonly List<Instruction> instructions;

        MethodBodyReader(MethodBase method)
        {
            this.method = method;

            this.body = method.GetMethodBody();
            if (this.body == null)
                throw new ArgumentException("Method has no body");

            var bytes = body.GetILAsByteArray();
            if (bytes == null)
                throw new ArgumentException("Can not get the body of the method");

            if (!(method is ConstructorInfo))
                method_arguments = method.GetGenericArguments();

            if (method.DeclaringType != null)
                type_arguments = method.DeclaringType.GetGenericArguments();

            this.parameters = method.GetParameters();
            this.locals = body.LocalVariables;
            this.module = method.Module;
            this.il = new ByteBuffer(bytes);
            this.instructions = new List<Instruction>((bytes.Length + 1) / 2);
        }

        void ReadInstructions()
        {
            Instruction previous = null;

            while (il.position < il.buffer.Length)
            {
                var instruction = new Instruction(il.position, ReadOpCode());

                ReadOperand(instruction);

                if (previous != null)
                {
                    instruction.Previous = previous;
                    previous.Next = instruction;
                }

                instructions.Add(instruction);
                previous = instruction;
            }

            ResolveBranches();
        }

        void ReadOperand(Instruction instruction)
        {
            switch (instruction.OpCode.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.InlineSwitch:
                    int length = il.ReadInt32();
                    int base_offset = il.position + (4 * length);
                    int[] branches = new int[length];
                    for (int i = 0; i < length; i++)
                        branches[i] = il.ReadInt32() + base_offset;

                    instruction.Operand = branches;
                    break;
                case OperandType.ShortInlineBrTarget:
                    instruction.Operand = (((sbyte)il.ReadByte()) + il.position);
                    break;
                case OperandType.InlineBrTarget:
                    instruction.Operand = il.ReadInt32() + il.position;
                    break;
                case OperandType.ShortInlineI:
                    if (instruction.OpCode == OpCodes.Ldc_I4_S)
                        instruction.Operand = (sbyte)il.ReadByte();
                    else
                        instruction.Operand = il.ReadByte();
                    break;
                case OperandType.InlineI:
                    instruction.Operand = il.ReadInt32();
                    break;
                case OperandType.ShortInlineR:
                    instruction.Operand = il.ReadSingle();
                    break;
                case OperandType.InlineR:
                    instruction.Operand = il.ReadDouble();
                    break;
                case OperandType.InlineI8:
                    instruction.Operand = il.ReadInt64();
                    break;
                case OperandType.InlineSig:
                    instruction.Operand = module.ResolveSignature(il.ReadInt32());
                    break;
                case OperandType.InlineString:
                    instruction.Operand = module.ResolveString(il.ReadInt32());
                    break;
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                    instruction.Operand = module.ResolveMember(il.ReadInt32(), type_arguments, method_arguments);
                    break;
                case OperandType.ShortInlineVar:
                    instruction.Operand = GetVariable(instruction, il.ReadByte());
                    break;
                case OperandType.InlineVar:
                    instruction.Operand = GetVariable(instruction, il.ReadInt16());
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        void ResolveBranches()
        {
            foreach (var instruction in instructions)
            {
                switch (instruction.OpCode.OperandType)
                {
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.InlineBrTarget:
                        instruction.Operand = GetInstruction(instructions, (int)instruction.Operand);
                        break;
                    case OperandType.InlineSwitch:
                        var offsets = (int[])instruction.Operand;
                        var branches = new Instruction[offsets.Length];
                        for (int j = 0; j < offsets.Length; j++)
                            branches[j] = GetInstruction(instructions, offsets[j]);

                        instruction.Operand = branches;
                        break;
                }
            }
        }

        static Instruction GetInstruction(List<Instruction> instructions, int offset)
        {
            var size = instructions.Count;
            if (offset < 0 || offset > instructions[size - 1].Offset)
                return null;

            int min = 0;
            int max = size - 1;
            while (min <= max)
            {
                int mid = min + ((max - min) / 2);
                var instruction = instructions[mid];
                var instruction_offset = instruction.Offset;

                if (offset == instruction_offset)
                    return instruction;

                if (offset < instruction_offset)
                    max = mid - 1;
                else
                    min = mid + 1;
            }

            return null;
        }

        object GetVariable(Instruction instruction, int index)
        {
            return TargetsLocalVariable(instruction.OpCode)
                ? (object)GetLocalVariable(index)
                : (object)GetParameter(index);
        }

        static bool TargetsLocalVariable(OpCode opcode)
        {
            return opcode.Name.Contains("loc");
        }

        LocalVariableInfo GetLocalVariable(int index)
        {
            return locals[index];
        }

        ParameterInfo GetParameter(int index)
        {
            return parameters[method.IsStatic ? index : index - 1];
        }

        OpCode ReadOpCode()
        {
            byte op = il.ReadByte();
            return op != 0xfe
                ? one_byte_opcodes[op]
                : two_bytes_opcodes[il.ReadByte()];
        }

        public static List<Instruction> GetInstructions(MethodBase method)
        {
            var reader = new MethodBodyReader(method);
            reader.ReadInstructions();
            return reader.instructions;
        }
    }

    class ByteBuffer
    {

        internal byte[] buffer;
        internal int position;

        public ByteBuffer(byte[] buffer)
        {
            this.buffer = buffer;
        }

        public byte ReadByte()
        {
            CheckCanRead(1);
            return buffer[position++];
        }

        public byte[] ReadBytes(int length)
        {
            CheckCanRead(length);
            var value = new byte[length];
            Buffer.BlockCopy(buffer, position, value, 0, length);
            position += length;
            return value;
        }

        public short ReadInt16()
        {
            CheckCanRead(2);
            short value = (short)(buffer[position]
                | (buffer[position + 1] << 8));
            position += 2;
            return value;
        }

        public int ReadInt32()
        {
            CheckCanRead(4);
            int value = buffer[position]
                | (buffer[position + 1] << 8)
                | (buffer[position + 2] << 16)
                | (buffer[position + 3] << 24);
            position += 4;
            return value;
        }

        public long ReadInt64()
        {
            CheckCanRead(8);
            uint low = (uint)(buffer[position]
                | (buffer[position + 1] << 8)
                | (buffer[position + 2] << 16)
                | (buffer[position + 3] << 24));

            uint high = (uint)(buffer[position + 4]
                | (buffer[position + 5] << 8)
                | (buffer[position + 6] << 16)
                | (buffer[position + 7] << 24));

            long value = (((long)high) << 32) | low;
            position += 8;
            return value;
        }

        public float ReadSingle()
        {
            if (!BitConverter.IsLittleEndian)
            {
                var bytes = ReadBytes(4);
                Array.Reverse(bytes);
                return BitConverter.ToSingle(bytes, 0);
            }

            CheckCanRead(4);
            float value = BitConverter.ToSingle(buffer, position);
            position += 4;
            return value;
        }

        public double ReadDouble()
        {
            if (!BitConverter.IsLittleEndian)
            {
                var bytes = ReadBytes(8);
                Array.Reverse(bytes);
                return BitConverter.ToDouble(bytes, 0);
            }

            CheckCanRead(8);
            double value = BitConverter.ToDouble(buffer, position);
            position += 8;
            return value;
        }

        void CheckCanRead(int count)
        {
            if (position + count > buffer.Length)
                throw new ArgumentOutOfRangeException();
        }
    }

    public sealed class Instruction
    {

        int offset;
        OpCode opcode;
        object operand;

        Instruction previous;
        Instruction next;

        public int Offset
        {
            get { return offset; }
        }

        public OpCode OpCode
        {
            get { return opcode; }
        }

        public object Operand
        {
            get { return operand; }
            internal set { operand = value; }
        }

        public Instruction Previous
        {
            get { return previous; }
            internal set { previous = value; }
        }

        public Instruction Next
        {
            get { return next; }
            internal set { next = value; }
        }

        public int Size
        {
            get
            {
                int size = opcode.Size;

                switch (opcode.OperandType)
                {
                    case OperandType.InlineSwitch:
                        size += (1 + ((int[])operand).Length) * 4;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        size += 8;
                        break;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineField:
                    case OperandType.InlineI:
                    case OperandType.InlineMethod:
                    case OperandType.InlineString:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                    case OperandType.ShortInlineR:
                        size += 4;
                        break;
                    case OperandType.InlineVar:
                        size += 2;
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        size += 1;
                        break;
                }

                return size;
            }
        }

        internal Instruction(int offset, OpCode opcode)
        {
            this.offset = offset;
            this.opcode = opcode;
        }

        public override string ToString()
        {
            var instruction = new StringBuilder();

            AppendLabel(instruction, this);
            instruction.Append(':');
            instruction.Append(' ');
            instruction.Append(opcode.Name);

            if (operand == null)
                return instruction.ToString();

            instruction.Append(' ');

            switch (opcode.OperandType)
            {
                case OperandType.ShortInlineBrTarget:
                case OperandType.InlineBrTarget:
                    AppendLabel(instruction, (Instruction)operand);
                    break;
                case OperandType.InlineSwitch:
                    var labels = (Instruction[])operand;
                    for (int i = 0; i < labels.Length; i++)
                    {
                        if (i > 0)
                            instruction.Append(',');

                        AppendLabel(instruction, labels[i]);
                    }
                    break;
                case OperandType.InlineString:
                    instruction.Append('\"');
                    instruction.Append(operand);
                    instruction.Append('\"');
                    break;
                default:
                    instruction.Append(operand);
                    break;
            }

            return instruction.ToString();
        }

        static void AppendLabel(StringBuilder builder, Instruction instruction)
        {
            builder.Append("IL_");
            builder.Append(instruction.offset.ToString("x4"));
        }
    }
}