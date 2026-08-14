using System.Globalization;
using System.Text;
using Blokemon.CardGen.Authority;
using Blokemon.CardGen.Domain;
using Blokemon.CardGen.Rendering;
using Blokemon.PackGen.Catalogue;
using Blokemon.PackGen.Domain;
using Blokemon.PackGen.Rendering;

if (args.Length is not 2)
{
    await Console.Error.WriteLineAsync("usage: Blokemon.Sheet <content-directory> <output.html>");
    return 1;
}

var (content, output) = (Path.GetFullPath(args[0]), args[1]);
var authorities = Path.Combine(content, "authorities");
var art = Path.Combine(content, "art");

var set = SetAuthority.Load(
    Path.Combine(authorities, "public-content.json"),
    Path.Combine(authorities, "mechanics.json"),
    Path.Combine(authorities, "printing.json"),
    art
);

var cards = CardDocument.Load(art);
var page = new StringBuilder();

// The page supplies what every object shares, which is what the emitted objects assume.
page.Append(Shell(Path.Combine(content, "fonts"), cards.Stylesheet));

Section("Collectibles", set.Blokemon.Select(cards.Build));
Section("Support", set.Supports.Select(cards.Build));
Section("Basic Energy", set.Energy.Select(cards.Build));
Section("Reverse", [cards.Build(set.Reverse)]);
Section("Type glyphs", Enum.GetValues<BlokemonType>().Select(TypeGlyphs.Glyph));

foreach (var stock in Enum.GetValues<PackStock>())
{
    var profile = PackProfile.Blokemon(stock);
    Section(
        $"Packaging &middot; {stock}",
        PackCatalogue.All.Select(pack => PackArt.Draw(pack, profile))
    );
}

await File.WriteAllTextAsync(output, page.ToString());
Console.WriteLine($"{output} ({new FileInfo(output).Length / 1024:N0} KB)");
return 0;

void Section(string title, IEnumerable<string> objects) =>
    _ = page.Append(CultureInfo.InvariantCulture, $"<h2>{title}</h2><div class=\"row\">")
        .AppendJoin(string.Empty, objects.Select(svg => $"<div class=\"slot\">{svg}</div>"))
        .Append("</div>");

static string Shell(string fonts, string stylesheet)
{
    var faces = string.Concat(
        new[]
        {
            ("GilliusADF-Regular.otf", "Gillius ADF", 400, "normal"),
            ("GilliusADF-Italic.otf", "Gillius ADF", 400, "italic"),
            ("GilliusADF-Bold.otf", "Gillius ADF", 700, "normal"),
            ("GilliusADF-BoldItalic.otf", "Gillius ADF", 700, "italic"),
            ("Jost-700-Bold.otf", "Jost", 700, "normal"),
            ("Jost-900-Black.otf", "Jost", 900, "normal"),
        }.Select(face =>
        {
            var data = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(fonts, face.Item1)));
            return $"@font-face{{font-family:'{face.Item2}';font-style:{face.Item4};font-weight:{face.Item3};font-display:block;src:url(data:font/otf;base64,{data}) format('opentype')}}";
        })
    );

    return """
        <!doctype html><meta charset="utf-8"><title>Blokemon sheet</title>
        <style>FACES
        STYLESHEET
        body{margin:0;background:#d8d2c6;font:15px system-ui,sans-serif;color:#1b1f1a}
        h2{margin:38px 18px 12px;font-size:20px;letter-spacing:.02em}
        .row{display:flex;flex-wrap:wrap;gap:14px;padding:0 18px;align-items:flex-end}
        .slot svg{display:block;width:230px;height:auto}
        .slot svg[width="24"]{width:64px;background:#faf7ef;padding:8px;border-radius:8px}
        </style>
        """.Replace("FACES", faces, StringComparison.Ordinal).Replace(
        "STYLESHEET",
        stylesheet,
        StringComparison.Ordinal
    );
}
