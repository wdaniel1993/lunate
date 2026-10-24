using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The coding harness's session creation: one file, named by the store-assigned id, with no
/// provisional file left in the sessions directory for the picker to trip over.
/// </summary>
public sealed class SessionCreationTests
{
    [Fact]
    public void CreateSession_writes_one_file_under_the_assigned_id()
    {
        using var temp = new TempDirectory();
        string directory = temp.File("sessions");

        Session session = HarnessFactory.CreateSession(directory, new Workspace(temp.Root));

        string expected = Path.Combine(directory, SessionPaths.SessionFileName(session.SessionId));
        Assert.Equal(expected, session.Path);
        Assert.Equal([expected], Directory.GetFiles(directory));

        Session reloaded = Session.Load(expected);
        Assert.Equal(session.SessionId, reloaded.SessionId);
        Assert.Empty(reloaded.Entries);
    }
}
