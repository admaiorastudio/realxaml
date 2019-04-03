using System;
using System.Collections.Generic;
using System.Text;

namespace AdMaiora.RealXaml.Client
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RunAfterXamlLoadAttribute : Attribute
    {
    }
}
