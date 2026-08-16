namespace Blokemon.PackGen.Rendering

open Blokemon.PackGen.Domain

/// A flexible wrapper drawn as a printed object.
module WrapperArt =

    let private sealShadow = 24.0

    let private crimp = 14.0

    let private creaseInset = 0.06

    let private glintInset = 0.25

    /// A fold in film: a lit edge followed immediately by its own shadow.
    type private Crease =
        { Angle: float
          From: float
          Peak: float
          Trough: float
          To: float
          Light: float
          Dark: float }

    let private crease (angle, start, peak, trough, finish, light, dark) =
        { Angle = angle
          From = start
          Peak = peak
          Trough = trough
          To = finish
          Light = light
          Dark = dark }

    // The six creases are authored rather than spread evenly, which would read as corduroy. Each
    // is a lit edge followed immediately by its own shadow, which is what a fold in film does.
    let private creases =
        [ crease (97.0, 0.18, 0.21, 0.23, 0.27, 0.5, 0.35)
          crease (84.0, 0.44, 0.47, 0.49, 0.53, 0.34, 0.28)
          crease (103.0, 0.66, 0.69, 0.71, 0.75, 0.4, 0.3)
          crease (75.0, 0.08, 0.10, 0.12, 0.15, 0.22, 0.2)
          crease (160.0, 0.30, 0.33, 0.35, 0.39, 0.2, 0.18)
          crease (168.0, 0.72, 0.75, 0.77, 0.81, 0.24, 0.2) ]

    let private overflow (face: FaceMetrics) inset =
        face.Width * (1.0 + inset * 2.0), face.Height * (1.0 + inset * 2.0)

    let private grainDefs (palette: Palette) =
        if palette.Fibre then
            Svg.stripes "fibre-warp" 72.0 4.0 1.0 "#5a3c14" 0.14
            + Svg.stripes "fibre-weft" -64.0 6.0 1.0 "#5a3c14" 0.10
        else
            ""

    let private defs (face: FaceMetrics) (palette: Palette) (sealHeight: float) =
        let w, h = face.Width, face.Height
        let cw, ch = overflow face creaseInset
        let sw, sh = overflow face glintInset

        let creaseGradients =
            creases
            |> List.indexed
            |> List.map (fun (index, fold) ->
                Svg.linear
                    $"crease-{index}"
                    fold.Angle
                    cw
                    ch
                    [ Stop.faded fold.From "#ffffff" 0.0
                      Stop.faded fold.Peak "#ffffff" fold.Light
                      Stop.faded fold.Trough "#000000" fold.Dark
                      Stop.faded fold.To "#000000" 0.0 ])
            |> String.concat ""

        [ $"""<clipPath id="body"><rect width="{Svg.n w}" height="{Svg.n h}" rx="3"/></clipPath>"""
          Svg.linear "stock" palette.StockAngle w h palette.Stock
          Svg.linear
              "form-across"
              90.0
              w
              h
              [ Stop.faded 0.0 "#000000" 0.46
                Stop.faded 0.07 "#000000" 0.16
                Stop.faded 0.24 "#ffffff" 0.10
                Stop.faded 0.44 "#ffffff" 0.16
                Stop.faded 0.62 "#ffffff" 0.06
                Stop.faded 0.88 "#000000" 0.20
                Stop.faded 1.0 "#000000" 0.5 ]
          Svg.linear
              "form-down"
              180.0
              w
              h
              [ Stop.faded 0.0 "#000000" 0.26
                Stop.faded 0.12 "#000000" 0.0
                Stop.faded 0.84 "#000000" 0.0
                Stop.faded 1.0 "#000000" 0.32 ]
          creaseGradients
          Svg.linear
              "glint-core"
              102.0
              sw
              sh
              [ Stop.faded 0.28 "#ffffff" 0.0
                Stop.faded 0.42 "#ffffff" 0.10
                Stop.faded 0.5 "#ffffff" 0.38
                Stop.faded 0.58 "#ffffff" 0.10
                Stop.faded 0.72 "#ffffff" 0.0 ]
          Svg.linear
              "glint-trail"
              102.0
              sw
              sh
              [ Stop.faded 0.64 "#ffffff" 0.0
                Stop.faded 0.73 "#ffffff" 0.13
                Stop.faded 0.82 "#ffffff" 0.0 ]
          Svg.linear
              "seal"
              180.0
              w
              sealHeight
              [ Stop.solid 0.0 palette.SealTop; Stop.solid 1.0 palette.SealBottom ]
          """<pattern id="rib" width="7" height="1" patternUnits="userSpaceOnUse"><rect width="2" height="1" fill="#000000" fill-opacity="0.34"/><rect x="2" width="2" height="1" fill="#ffffff" fill-opacity="0.2"/><rect x="4" width="3" height="1" fill="#000000" fill-opacity="0.1"/></pattern>"""
          Svg.linear
              "seal-cast-top"
              180.0
              w
              sealShadow
              [ Stop.faded 0.0 "#000000" 0.48; Stop.faded 1.0 "#000000" 0.0 ]
          Svg.linear
              "seal-cast-bot"
              0.0
              w
              sealShadow
              [ Stop.faded 0.0 "#000000" 0.48; Stop.faded 1.0 "#000000" 0.0 ]
          """<radialGradient id="ground"><stop offset="0" stop-color="#0a0802" stop-opacity="0.5"/><stop offset="0.7" stop-color="#0a0802" stop-opacity="0"/></radialGradient>"""
          grainDefs palette
          PrintedFace.defs ]
        |> String.concat ""

    // Gradients are declared in user space from the origin, and the animated class owns the CSS
    // transform, so an overflowing layer is drawn at the origin inside its own offset child.
    let private overflowing
        (blend: string)
        (opacity: float)
        (face: FaceMetrics)
        (inset: float)
        (animated: string option)
        (layers: string)
        =
        let mark =
            match animated with
            | Some name -> $" class=\"{name}\""
            | None -> ""

        let x, y = -face.Width * inset, -face.Height * inset

        $"""<g{mark} style="{blend}" opacity="{Svg.n opacity}"><g transform="translate({Svg.n x} {Svg.n y})">{layers}</g></g>"""

    let private creaseLayers (face: FaceMetrics) (palette: Palette) =
        let w, h = overflow face creaseInset

        let layers =
            creases
            |> List.indexed
            |> List.map (fun (index, _) -> Svg.rect 0.0 0.0 w h (Svg.url $"crease-{index}"))
            |> String.concat ""

        overflowing "mix-blend-mode:overlay" palette.Crinkle face creaseInset None layers

    let private specular (face: FaceMetrics) (palette: Palette) =
        if palette.Specular = 0.0 then
            ""
        else
            let w, h = overflow face glintInset

            let sweep =
                Svg.rect 0.0 0.0 w h (Svg.url "glint-core")
                + Svg.rect 0.0 0.0 w h (Svg.url "glint-trail")

            overflowing
                "mix-blend-mode:screen"
                palette.Specular
                face
                glintInset
                (Some "glint")
                sweep

    let private grain (face: FaceMetrics) (palette: Palette) =
        if palette.Fibre then
            let warp = Svg.rect 0.0 0.0 face.Width face.Height (Svg.url "fibre-warp")
            let weft = Svg.rect 0.0 0.0 face.Width face.Height (Svg.url "fibre-weft")
            $"""<g style="mix-blend-mode:multiply" opacity="0.5">{warp}{weft}</g>"""
        else
            ""

    let private zip (resealable: bool) (face: FaceMetrics) =
        if resealable then
            $"""<g><rect x="0" y="56" width="{Svg.n face.Width}" height="15" fill="url(#rib)"/><rect x="0" y="56" width="{Svg.n face.Width}" height="1" fill="#ffffff" fill-opacity="0.5"/></g>"""
        else
            ""

    let private sealShadows (width: float) (height: float) (sealHeight: float) =
        Svg.rect 0.0 sealHeight width sealShadow (Svg.url "seal-cast-top")
        + Svg.rect 0.0 (height - sealHeight - sealShadow) width sealShadow (Svg.url "seal-cast-bot")

    // The crimp is cut into the outer edge, so the top seal is scalloped along its top and the
    // bottom seal is the same shape turned over.
    let private seal (width: float) (y: float) (height: float) (flip: bool) =
        let steps = int (ceil (width / crimp))

        let scallops =
            [ 0 .. steps - 1 ]
            |> List.map (fun step -> Svg.n (min crimp (width - float step * crimp)))
            |> List.map (fun span -> $"a7 3 0 0 0 {span} 0")
            |> String.concat ""

        let path = $"M0 0{scallops}V{Svg.n height}H0Z"

        let turn =
            if flip then
                $"translate(0 {Svg.n (y + height)}) scale(1 -1)"
            else
                $"translate(0 {Svg.n y})"

        $"""<g transform="{turn}"><path d="{path}" fill="url(#seal)"/><path d="{path}" fill="url(#rib)"/></g>"""

    let private tab (resealable: bool) (booster: bool) =
        if not resealable then
            ""
        else
            let x, w = if booster then 145.0, 170.0 else 95.0, 150.0
            let hole = x + w / 2.0 - 17.0

            [ $"""<mask id="hang"><rect x="{Svg.n x}" y="-30" width="{Svg.n w}" height="52" fill="#ffffff"/><rect x="{Svg.n hole}" y="-14" width="34" height="17" rx="8.5" fill="#000000"/></mask>"""
              $"""<g mask="url(#hang)"><path d="M{Svg.n x} 22V-22a8 8 0 0 1 8-8h{Svg.n (w - 16.0)}a8 8 0 0 1 8 8v44Z" fill="url(#seal)"/><rect x="{Svg.n x}" y="-30" width="{Svg.n w}" height="10" rx="8" fill="#ffffff" fill-opacity="0.26"/></g>""" ]
            |> String.concat "\n"

    let private ground (width: float) (height: float) (drop: float) =
        $"""<ellipse cx="{Svg.n (width / 2.0)}" cy="{Svg.n (height + drop - 18.0)}" rx="{Svg.n (width * 0.44)}" ry="18" fill="url(#ground)"/>"""

    /// Draws a wrapper.
    let Draw (pack: Pack) (profile: PackProfile) (size: WrapperSize) (resealable: bool) =
        let booster = size = WrapperSize.Booster
        let face = if booster then FaceMetrics.booster else FaceMetrics.small
        let palette = Palette.forMaterial (pack.MaterialUnder profile.Stock)
        let height = if booster then 830.0 else 600.0
        let bodyTop, sealHeight = if booster then 40.0, 44.0 else 34.0, 34.0
        let groundDrop = if booster then 24.0 else 18.0
        let top = if resealable then -34.0 else -8.0

        let art =
            [ ground face.Width height groundDrop
              $"""<g clip-path="url(#body)" transform="translate(0 {Svg.n bodyTop})">"""
              Svg.rect 0.0 0.0 face.Width face.Height (Svg.url "stock")
              Svg.rect 0.0 0.0 face.Width face.Height (Svg.url "form-across")
              Svg.rect 0.0 0.0 face.Width face.Height (Svg.url "form-down")
              creaseLayers face palette
              grain face palette
              specular face palette
              PrintedFace.print pack profile palette face
              zip resealable face
              "</g>"
              sealShadows face.Width height sealHeight
              seal face.Width 0.0 sealHeight false
              seal face.Width (height - sealHeight) sealHeight true
              tab resealable booster ]
            |> String.concat ""

        Document.wrap
            { X = 0.0
              Y = top
              Width = face.Width
              Height = height + groundDrop - top
              Title = $"{profile.Noun} {profile.Name pack.Key} wrapper"
              Defs = defs face palette sealHeight
              Art = art
              Style =
                if palette.Specular > 0.0 then
                    Document.glint (face.Width * (1.0 + glintInset * 2.0) * 1.45) pack.Glint
                else
                    ""
              Scope = $"{PackKey.slug pack.Key}-{profile.Stock.ToString().ToLowerInvariant()}" }
