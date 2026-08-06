namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's CommandExecutor interface. Java's package-private
// `interface CommandExecutor { int run(List<String> command, Path directory) throws Exception; }`
// maps 1:1 to this C# interface. Java's package-private visibility is unavailable in the single-
// assembly design where the `internal` modifier is banned (ratified decision C1), so the interface is
// `public`. Signature mapping: Java `List<String>` -> `IReadOnlyList<string>`; Java `Path directory`
// -> `string workingDirectory`; Java `throws Exception` -> exceptions propagate naturally (no C#
// throws clause). The method stays synchronous returning `int` to mirror Java's `run` exactly -- the
// async stdout drain is an implementation detail of ProcessCommandExecutor, not a signature change.
public interface ICommandExecutor
{
    // Ports run(List<String>, Path): launches command in workingDirectory, returns the child exit code.
    int Run(IReadOnlyList<string> command, string workingDirectory);
}
