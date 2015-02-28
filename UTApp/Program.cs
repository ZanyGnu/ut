using ClrTest.Reflection;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UTApp
{
    [TestClass]
    public class Program
    {
        static void Main(string[] args)
        {
            // Make sure to run the test as a part of the visual studio test runner. (File->Test->)

            MethodInfo mi = typeof(Program).GetMethod("TestSleep");
            MethodBody mb = mi.GetMethodBody();
            Console.WriteLine("\r\nMethod: {0}", mi);
            List<Instruction> instructions = MethodBodyReader.GetInstructions(mi);
            foreach (Instruction i in instructions)
            {
                Console.WriteLine(i.ToString());
            }
            Console.WriteLine("\r\nMethod: {0}", mi);

            ReadableILStringVisitor v = new ReadableILStringVisitor(new ReadableILStringToTextWriter(System.Console.Out));
            ILReader reader = new ILReader(mi);
            reader.Accept(v);
            foreach (ILInstruction il in reader)
            {
                il.ToString();
            }
            
        }

        static void PrintInstruction(Instruction instruction)
        {
            Console.Write("{0}: {1} ",
                instruction.Offset,
                instruction.OpCode.Name);

            switch (instruction.OpCode.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.InlineSwitch:
                    var branches = instruction.Operand as int[];
                    for (int i = 0; i < branches.Length; i++)
                    {
                        if (i > 0)
                            Console.Write(", ");
                        Console.Write(branches[i]);
                    }
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.InlineBrTarget:
                    Console.Write((int)instruction.Operand);
                    break;
                case OperandType.InlineString:
                    Console.Write("\"{0}\"", instruction.Operand);
                    break;
                default:
                    Console.WriteLine(instruction.Operand);
                    break;
            }

            Console.WriteLine();
        }

        public void FindMethod(MethodInfo method)
        {
            byte[] ilBytes = method.GetMethodBody().GetILAsByteArray();
            //ilBytes
        }

        [TestMethod]
        public void TestWithFake()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            using (ShimsContext.Create())
            {
                //System.Threading.Fakes.Thread.sFakes.Thread.Sleep
                System.Threading.Fakes.ShimThread.SleepTimeSpan = (arg) =>
                {
                    return;
                };

                TestSleep(TimeSpan.FromSeconds(10));
            }

            // We expect the elapsed time to be much less than 10 seconds
            Console.WriteLine("Elasped time: " + sw.Elapsed);
        }

        [TestMethod]
        public void TestWithoutFake()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            TestSleep(TimeSpan.FromSeconds(10));

            Thread.Sleep(TimeSpan.FromSeconds(10));
            Thread.Sleep(1);

            // We expect the elapsed time to be 10 seconds
            Console.WriteLine("Elasped time: " + sw.Elapsed);
        }

        public static void TestSleep(TimeSpan timeToSleep)
        {
            Thread.Sleep(timeToSleep);
        }

        //public static void TestSleep()
        //{
        //    Thread.Sleep(1);
        //}
    }
}
