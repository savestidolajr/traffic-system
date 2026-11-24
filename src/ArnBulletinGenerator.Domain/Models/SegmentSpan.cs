namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     One HERE topology segment a record references, in HERE's FULL reference form (tile included)
///     with the length HERE reported for it.
///     <para>
///         The length is the weight road identity is resolved on: a record clipping 100 m of a side
///         street and running 900 m along an arterial is on the arterial, and counting references
///         instead of metres gets that backwards. HERE returns the value as a number that may be
///         fractional, so it is carried as a double.
///     </para>
/// </summary>
/// <param name="Ref">HERE's full segment reference, e.g.
///     <c>hrn:here:data::olp-here:rib-2:9203::365362849:here:cm:segment:914839809#-0.02..0.98</c>.</param>
/// <param name="LengthMeters">The length HERE reported for this segment, or 0 when it reported none.</param>
public readonly record struct SegmentSpan(string Ref, double LengthMeters)
{
    /// <summary>
    ///     HERE's direction sign for this segment: <c>+</c> along the segment's digitisation,
    ///     <c>-</c> against it, <c>*</c> undirected, or null when the reference carries none. HERE
    ///     stamps one on 100% of measured references (2,209 corpus, 123 live).
    /// </summary>
    public char? DirectionSign
    {
        get
        {
            int hash = Ref.IndexOf('#', StringComparison.Ordinal);
            if (hash < 0 || hash + 1 >= Ref.Length)
                return null;
            char sign = Ref[hash + 1];
            return sign is '+' or '-' or '*' ? sign : null;
        }
    }
}
