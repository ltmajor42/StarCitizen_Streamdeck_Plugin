using System;
using System.Runtime.InteropServices;
using WindowsInput.Native;

namespace WindowsInput
{
    /// <summary>
    /// Implements the <see cref="IInputMessageDispatcher"/> by calling SendInput.
    /// </summary>
    internal class WindowsInputMessageDispatcher : IInputMessageDispatcher
    {
        /// <summary>
        /// Dispatches the specified list of <see cref="INPUT"/> messages in their specified order by issuing a single called to SendInput.
        /// </summary>
        /// <param name="inputs">The list of <see cref="INPUT"/> messages to be dispatched.</param>
        /// <exception cref="ArgumentException">If the <paramref name="inputs"/> array is empty.</exception>
        /// <exception cref="ArgumentNullException">If the <paramref name="inputs"/> array is null.</exception>
        /// <exception cref="Exception">If the any of the commands in the <paramref name="inputs"/> array could not be sent successfully.</exception>
        public void DispatchInput(INPUT[] inputs)
        {
            if (inputs == null) throw new ArgumentNullException("inputs");
            if (inputs.Length == 0) throw new ArgumentException("The input array was empty", "inputs");
            var successful = NativeMethods.SendInput((UInt32)inputs.Length, inputs, Marshal.SizeOf(typeof (INPUT)));
            if (successful != inputs.Length)
            {
                int last = Marshal.GetLastWin32Error();
                // build a small summary of first few inputs
                try
                {
                    int preview = Math.Min(inputs.Length, 6);
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < preview; i++)
                    {
                        var inp = inputs[i];
                        sb.AppendFormat("[Type={0}]", inp.Type);
                        if (i < preview - 1) sb.Append(',');
                    }

                    throw new Exception($"SendInput failed: requested={inputs.Length}, sent={successful}, lastError={last}. InputsPreview={sb}");
                }
                catch
                {
                    throw new Exception($"SendInput failed: requested={inputs.Length}, sent={successful}, lastError={last}.");
                }
            }
        }
    }
}