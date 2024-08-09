using System.Diagnostics;
using System.Reflection;

namespace MassTransitConsumer;

internal class Instrumentation
{
    private static readonly AssemblyName AssemblyName = typeof(Instrumentation).Assembly.GetName();
    public static readonly ActivitySource ActivitySource = new(AssemblyName.Name!, AssemblyName.Version!.ToString());
}
