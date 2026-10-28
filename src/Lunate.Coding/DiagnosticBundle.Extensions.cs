using System.Globalization;
using System.Text;
using Lunate.Extensibility;

namespace Lunate.Coding;

internal static partial class DiagnosticBundle
{
    private static void AppendExtensions(StringBuilder text, DiagnosticBundleInput input)
    {
        Section(text, "extensions");
        text.Line(input.ExtensionsText);
        text.Line("single-file candidates:");
        if (input.SingleFileExtensions.Count == 0)
        {
            text.Line("  (none)");
        }
        else
        {
            foreach (SingleFileExtension extension in input.SingleFileExtensions)
            {
                AppendSingleFile(text, extension);
            }
        }

        text.Line("errors:");
        if (input.ExtensionErrors.Count == 0)
        {
            text.Line("  (none)");
            return;
        }

        foreach (ExtensionSessionError error in input.ExtensionErrors)
        {
            text.Line("  " + (error.ExtensionId ?? "session") + ": " + Truncate(error.Message));
        }
    }

    private static void AppendSingleFile(StringBuilder text, SingleFileExtension extension)
    {
        text.Line("  " + extension.Path);
        text.Line(
            "    id: "
                + (extension.Id ?? "(none)")
                + ", version: "
                + (extension.Version ?? "(none)")
                + ", scope: "
                + Lower(extension.Scope)
        );
        text.Line(
            "    state: "
                + Lower(extension.State)
                + (
                    extension.Reason is { Length: > 0 } reason
                        ? ", reason: " + reason
                        : string.Empty
                )
        );
        text.Line("    digest: " + Value(extension.Digest));
        text.Line("    source digest: " + Value(extension.SourceDigest));
        text.Line("    lock hash: " + Value(extension.LockHash));
        if (extension.Lock is not { } @lock)
        {
            text.Line("    packages: (no lock)");
            return;
        }

        text.Line("    packages: " + @lock.Packages.Count.ToString(CultureInfo.InvariantCulture));
        foreach (ExtensionLockPackage package in @lock.Packages)
        {
            text.Line(
                "      "
                    + package.Id
                    + " "
                    + package.Version
                    + (package.Direct ? " direct" : string.Empty)
            );
        }

        text.Line("    dependency edges:");
        var edges = 0;
        foreach (ExtensionLockPackage package in @lock.Packages)
        {
            foreach (ExtensionLockDependency dependency in package.Dependencies)
            {
                text.Line(
                    "      " + package.Id + " -> " + dependency.Id + " [" + dependency.Range + "]"
                );
                edges++;
            }
        }

        if (edges == 0)
        {
            text.Line("      (none)");
        }
    }
}
