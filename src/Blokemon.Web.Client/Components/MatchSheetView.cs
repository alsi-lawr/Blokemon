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
    // One step of a question, on the surface it is asked on.
    //
    // A step is centred over the table only when it answers itself: a decision the match posed
    // whose candidates are cards the table cannot show, so the sheet holds them, or one answered
    // by its own buttons. A step asked OF the table must keep out of the middle, because the cards
    // it is asking for are underneath - a player told to choose a target would be choosing from
    // behind the words. It goes beside the table instead, where every other step of every
    // player-driven move already goes.
    public static MatchSheetView ChoiceStep(
        MatchFrameView frame,
        MatchActionView pending,
        MatchChoiceRequirementView requirement,
        bool posed,
        string? progress,
        string? effect,
        string? stepBackLabel
    ) =>
        new(
            MatchSheetMode.Choice,
            MatchText.PublicText(requirement.Label),
            MatchText.MoveBeingAnswered(pending),
            progress,
            effect,
            stepBackLabel,
            posed && !MatchTable.AsksTheTable(frame, requirement),
            []
        );

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
