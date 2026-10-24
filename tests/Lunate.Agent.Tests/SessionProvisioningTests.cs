namespace Lunate.Agent.Tests;

/// <summary>
/// Creating a session at its store-assigned name: the header is written to a provisional file, the
/// file is renamed to <c>&lt;sessionId&gt;.jsonl</c>, and the reload follows. Windows refuses the
/// rename while the header write still holds a handle, and refuses the reload while any other
/// writer does, so nothing provisional may survive the call.
/// </summary>
public sealed class SessionProvisioningTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_leaves_only_the_id_named_session_file()
    {
        using var temp = new TempDirectory();
        string directory = temp.File("sessions");

        Session session = SessionProvisioning.Create(
            directory,
            provisional => Session.Create(provisional, "/work", new FixedTimeProvider(Start))
        );

        string expected = Path.Combine(directory, SessionPaths.SessionFileName(session.SessionId));
        Assert.Equal(expected, session.Path);
        Assert.Equal([expected], Directory.GetFiles(directory));

        Session reloaded = Session.Load(expected);
        Assert.Equal(session.SessionId, reloaded.SessionId);
        Assert.Empty(reloaded.Entries);
    }

    [Fact]
    public void A_create_that_never_renames_leaves_no_provisional_file()
    {
        using var temp = new TempDirectory();
        string directory = temp.File("sessions");

        Assert.Throws<InvalidOperationException>(() =>
            SessionProvisioning.Create(
                directory,
                provisional =>
                {
                    _ = Session.Create(provisional, "/work", new FixedTimeProvider(Start));
                    throw new InvalidOperationException("the rename never ran");
                }
            )
        );

        Assert.Empty(Directory.GetFiles(directory));
    }
}
