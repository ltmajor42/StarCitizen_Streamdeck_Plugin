// Provides minimal platform compatibility attributes when targeting frameworks
// that do not ship them (e.g., older Visual Studio analyzers).
#if !NET5_0_OR_GREATER
using System;

namespace System.Runtime.Versioning
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Constructor |
                    AttributeTargets.Method | AttributeTargets.Struct | AttributeTargets.Interface |
                    AttributeTargets.Delegate | AttributeTargets.Enum | AttributeTargets.Property |
                    AttributeTargets.Event, Inherited = false, AllowMultiple = true)]
    internal sealed class SupportedOSPlatformAttribute : Attribute
    {
        public SupportedOSPlatformAttribute(string platformName) { }
    }
}
#endif
