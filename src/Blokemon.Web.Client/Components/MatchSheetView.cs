using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

public enum MatchSheetMode
{
    Forced,
    Destination,
    Actions,
    Choice,
    Confirm,
}

// The sheet the match page has decided to show, already worded. The sheet presenter prints it
// and reports what was pressed; it never decides what the sheet should say.
public sealed record MatchSheetView(
    MatchSheetMode Mode,
    string Heading,
    string? Eyebrow,
    string? Detail,
    string? Effect,
    string? CancelLabel,
    bool Centred,
    MatchActionView[] Options
)
{
    // What a decision the match posed holds on when the answer it sent came back refused. There
    // is nothing on the table to try again with, so it asks the question the decision carries and
    // offers the one press that sends the answer again: no list of options, no way to dismiss
    // something the match is waiting on, and nowhere for the engine's own name for the decision.
    public static MatchSheetView PosedRetry(MatchActionView decision) =>
        new(
            MatchSheetMode.Confirm,
            MatchText.PosedHeading(decision),
            "Required",
            null,
            null,
            null,
            true,
            []
        );
}

// The answers given to the open choice step so far, projected for printing: the page keeps the
// draft itself and names the cards, so the presenter reads values rather than match state.
public sealed record MatchChoiceView(
    int Amount,
    string? MechanicalType,
    string? EffectId,
    MatchAttachmentView[] Attachments
);

public sealed record MatchAttachmentView(
    string EnergyCardInstanceId,
    string EnergyName,
    string TargetName
);
