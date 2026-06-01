using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class MascotSizeTests
{
    [Theory]
    [InlineData(MascotSize.Compact)]
    [InlineData(MascotSize.Large)]
    public void Every_phase_has_nonempty_multiline_frames(MascotSize size)
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
        {
            var frames = MascotSprite.Frames(p, size);
            Assert.NotEmpty(frames);
            Assert.All(frames, f => Assert.NotEmpty(f)); // cada frame = array de líneas no vacío
        }
    }

    [Fact]
    public void Large_is_taller_than_compact()
    {
        var c = MascotSprite.Frames(SessionPhase.Idle, MascotSize.Compact)[0];
        var l = MascotSprite.Frames(SessionPhase.Idle, MascotSize.Large)[0];
        Assert.True(l.Length > c.Length); // large tiene más líneas
    }

    [Fact]
    public void Parse_size_falls_back_to_compact()
    {
        Assert.Equal(MascotSize.Compact, MascotSprite.ParseSize("nope"));
        Assert.Equal(MascotSize.Large, MascotSprite.ParseSize("large"));
    }
}
