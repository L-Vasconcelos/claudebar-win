using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class MascotSpriteTests
{
    [Fact]
    public void Every_phase_has_at_least_one_frame()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
        {
            var frames = MascotSprite.Frames(p);
            Assert.NotEmpty(frames);
            Assert.All(frames, f => Assert.False(string.IsNullOrEmpty(f)));
        }
    }

    [Fact]
    public void Idle_is_a_single_static_frame()
        => Assert.Single(MascotSprite.Frames(SessionPhase.Idle));

    [Fact]
    public void Processing_animates_with_multiple_frames()
        => Assert.True(MascotSprite.Frames(SessionPhase.Processing).Count > 1);

    [Fact]
    public void Label_key_is_defined_for_every_phase()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
            Assert.False(string.IsNullOrEmpty(MascotSprite.LabelKey(p)));
    }
}
