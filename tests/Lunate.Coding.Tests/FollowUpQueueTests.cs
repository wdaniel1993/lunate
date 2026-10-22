using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class FollowUpQueueTests
{
    [Fact]
    public void A_queued_entry_keeps_text_and_attachments()
    {
        var queue = new FollowUpQueue();
        var image = new ImageAttachment("image/png", 2, 3, new byte[] { 1, 2, 3 });
        var input = new UserInput("look [image 2x3]", [image]);

        Assert.True(queue.TryEnqueue(new QueuedFollowUp("look @shot.png", input)));

        QueuedFollowUp entry = Assert.Single(queue.Entries);
        Assert.Equal("look @shot.png", entry.EditorText);
        Assert.Equal("look [image 2x3]", entry.Input.Text);
        Assert.Same(image, Assert.Single(entry.Input.Images!));
    }

    [Fact]
    public void The_sixth_entry_is_refused_and_the_first_five_stay()
    {
        var queue = new FollowUpQueue();
        for (var index = 0; index < FollowUpQueue.Capacity; index++)
        {
            Assert.True(queue.TryEnqueue(Entry($"message {index}")));
        }

        Assert.False(queue.TryEnqueue(Entry("sixth")));

        Assert.Equal(FollowUpQueue.Capacity, queue.Count);
        Assert.Equal(
            ["message 0", "message 1", "message 2", "message 3", "message 4"],
            queue.Entries.Select(entry => entry.Input.Text)
        );
    }

    [Fact]
    public void Entries_run_from_the_head_and_keep_their_order()
    {
        var queue = new FollowUpQueue();
        queue.TryEnqueue(Entry("first"));
        queue.TryEnqueue(Entry("second"));

        Assert.True(queue.TryDequeue(out QueuedFollowUp? head));
        Assert.Equal("first", head!.Input.Text);
        Assert.Equal(["second"], queue.DrainEditorTexts());
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Draining_returns_every_editor_line_in_order()
    {
        var queue = new FollowUpQueue();
        queue.TryEnqueue(new QueuedFollowUp("first @a.png", new UserInput("first [image 1x1]")));
        queue.TryEnqueue(new QueuedFollowUp("second", new UserInput("second")));

        Assert.Equal(["first @a.png", "second"], queue.DrainEditorTexts());
        Assert.Equal(0, queue.Count);
    }

    private static QueuedFollowUp Entry(string text) => new(text, new UserInput(text));
}
