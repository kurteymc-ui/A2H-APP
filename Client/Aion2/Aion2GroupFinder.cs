using System.Text;

namespace AionDPS.Aion2;

/// <summary>What the group finder told: a player joined the group (the own character too, on joining someone's), or the group is going in.</summary>
public enum GroupNoticeKind
{
    Joined,
    Entering,
}

/// <param name="Name">The player who joined (null for <see cref="GroupNoticeKind.Entering"/>).</param>
public readonly record struct GroupNotice(GroupNoticeKind Kind, string? Name, DateTime At);

/// <summary>
/// The group finder's frames that matter while the game is in the background, so the meter can call the player back.
/// Read from the owner's recordings of 2026-11-01 and -02 (three groups formed through the finder):
/// <list type="bullet">
/// <item><c>0x0b97</c>: a member came into the group: <c>0c | slot u8 | id u32 | u16 0 | u16 | name (u8 length, UTF-8) | …</c>. It names the
/// player who joined: another player when someone joined the owner's group, the owner himself (twice in one session, slots 3 and 2) when he
/// was taken into someone else's. It came 3 to 15 s before the group went in.</item>
/// <item><c>0x0092</c> with <c>00</c> after the opcode: the group's list with the instance's ids as it goes in (sent again a few seconds later,
/// and with <c>01</c> on the way out).</item>
/// </list>
/// A direct party invite (not through the finder) is not in the recordings yet.
/// </summary>
public static class Aion2GroupFinder
{
    public const int JoinOpcode = 0x0b97;
    public const int EnterOpcode = 0x0092;

    /// <summary>One "going in" per group: the list comes twice within a few seconds.</summary>
    private static readonly TimeSpan EnterRepeat = TimeSpan.FromSeconds(30);

    private static DateTime _lastEnter = DateTime.MinValue;

    /// <summary>Raised on the capture thread.</summary>
    public static event Action<GroupNotice>? Noticed;

    /// <summary>The name in a join frame (see the class remarks), or null when the frame is not of that shape.</summary>
    public static string? JoinedName(ReadOnlySpan<byte> frame)
    {
        const int at = 12;
        if (frame.Length < at + 1 + 2 || frame[2] != 0x0c)
        {
            return null;
        }

        int length = frame[at];
        if (length is < 2 or > 32 || frame.Length < at + 1 + length)
        {
            return null;
        }

        try
        {
            string name = new UTF8Encoding(false, true).GetString(frame.Slice(at + 1, length));
            return name.All(c => char.IsLetterOrDigit(c)) ? name : null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    public static void Note(int opcode, ReadOnlySpan<byte> frame, DateTime at)
    {
        if (opcode == JoinOpcode && JoinedName(frame) is string name)
        {
            Noticed?.Invoke(new GroupNotice(GroupNoticeKind.Joined, name, at));
        }
        else if (opcode == EnterOpcode && frame.Length > 40 && frame[2] == 0x00)
        {
            if (at - _lastEnter < EnterRepeat && at >= _lastEnter)
            {
                return;
            }

            _lastEnter = at;
            Noticed?.Invoke(new GroupNotice(GroupNoticeKind.Entering, null, at));
        }
    }
}
