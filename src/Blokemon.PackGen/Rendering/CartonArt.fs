namespace Blokemon.PackGen.Rendering

open System
open Blokemon.PackGen.Domain

/// A rigid carton drawn as a printed object.
module CartonArt =

    let private boxWidth = 360.0

    let private boxHeight = 500.0

    let private boxDepth = 118.0

    let private stage = 640.0

    let private pitch = -14.0

    let private yaw = -27.0

    let private gauge = 7.0

    let private seat = 14.0

    /// The panel of a carton or of its inner tray a printed face lies on.
    [<RequireQualifiedAccess>]
    type private PanelName =
        | Front
        | Side
        | Top
        | Bottom
        | TrayFront
        | TraySide
        | TrayLeft
        | TrayBack
        | TrayFloor

        /// The token the panel's definitions are named by.
        member this.Token =
            match this with
            | PanelName.Front -> "front"
            | PanelName.Side -> "side"
            | PanelName.Top -> "top"
            | PanelName.Bottom -> "bottom"
            | PanelName.TrayFront -> "tray-front"
            | PanelName.TraySide -> "tray-side"
            | PanelName.TrayLeft -> "tray-left"
            | PanelName.TrayBack -> "tray-back"
            | PanelName.TrayFloor -> "tray-floor"

    /// One flat face of the rig, and where its printing lands in the turned box.
    type private Panel =
        { Name: PanelName
          Across: float
          Down: float
          Normal: float * float * float
          At: float -> float -> float * float * float
          Shade: string option
          ShadeAlpha: float }

        member this.Centre = this.At (this.Across / 2.0) (this.Down / 2.0)

    let private faces =
        [ { Name = PanelName.Front
            Across = boxWidth
            Down = boxHeight
            Normal = (0.0, 0.0, 1.0)
            At = (fun u v -> (u - 180.0, v - 250.0, 59.0))
            Shade = None
            ShadeAlpha = 0.0 }
          { Name = PanelName.Side
            Across = boxDepth
            Down = boxHeight
            Normal = (1.0, 0.0, 0.0)
            At = (fun u v -> (180.0, v - 250.0, 59.0 - u))
            Shade = Some "#060a04"
            ShadeAlpha = 0.46 }
          { Name = PanelName.Top
            Across = boxWidth
            Down = boxDepth
            Normal = (0.0, -1.0, 0.0)
            At = (fun u v -> (u - 180.0, -250.0, v - 59.0))
            Shade = Some "#fffcf0"
            ShadeAlpha = 0.17 }
          { Name = PanelName.Bottom
            Across = boxWidth
            Down = boxDepth
            Normal = (0.0, 1.0, 0.0)
            At = (fun u v -> (u - 180.0, 250.0, 59.0 - v))
            Shade = Some "#040602"
            ShadeAlpha = 0.72 } ]

    // The tray keeps the carton's rig, so the lid art and the tray art line up exactly when one
    // is drawn over the other. It is a board gauge smaller on every side, seats below the lid's
    // top, and shows its shaded interior through the open mouth.
    let private trayFaces =
        let width = boxWidth - 2.0 * gauge
        let depth = boxDepth - 2.0 * gauge
        let down = boxHeight - seat
        let rim = boxHeight / 2.0 - down
        let innerWidth = width - 2.0 * gauge
        let innerDepth = depth - 2.0 * gauge
        let floorLevel = boxHeight / 2.0 - gauge
        let cavity = floorLevel - rim

        [ { Name = PanelName.TrayFront
            Across = width
            Down = down
            Normal = (0.0, 0.0, 1.0)
            At = (fun u v -> (u - width / 2.0, v + rim, depth / 2.0))
            Shade = None
            ShadeAlpha = 0.0 }
          { Name = PanelName.TraySide
            Across = depth
            Down = down
            Normal = (1.0, 0.0, 0.0)
            At = (fun u v -> (width / 2.0, v + rim, depth / 2.0 - u))
            Shade = Some "#060a04"
            ShadeAlpha = 0.46 }
          { Name = PanelName.TrayLeft
            Across = innerDepth
            Down = cavity
            Normal = (1.0, 0.0, 0.0)
            At = (fun u v -> (-(innerWidth / 2.0), v + rim, innerDepth / 2.0 - u))
            Shade = Some "#030c07"
            ShadeAlpha = 0.58 }
          { Name = PanelName.TrayBack
            Across = innerWidth
            Down = cavity
            Normal = (0.0, 0.0, 1.0)
            At = (fun u v -> (u - innerWidth / 2.0, v + rim, -(innerDepth / 2.0)))
            Shade = Some "#04100a"
            ShadeAlpha = 0.5 }
          { Name = PanelName.TrayFloor
            Across = innerWidth
            Down = innerDepth
            Normal = (0.0, -1.0, 0.0)
            At = (fun u v -> (u - innerWidth / 2.0, floorLevel, innerDepth / 2.0 - v))
            Shade = Some "#020805"
            ShadeAlpha = 0.66 } ]

    let private cosDegrees (degrees: float) = Math.Cos(degrees * Math.PI / 180.0)

    let private sinDegrees (degrees: float) = Math.Sin(degrees * Math.PI / 180.0)

    // The rig turns the box and the projection is orthographic, which keeps every face an exact
    // parallelogram so its printing maps onto it without distortion.
    let private rotate (x, y, z) =
        let cy, sy = cosDegrees yaw, sinDegrees yaw
        let x1, z1 = x * cy + z * sy, z * cy - x * sy
        let cx, sx = cosDegrees pitch, sinDegrees pitch

        x1, y * cx - z1 * sx, y * sx + z1 * cx

    let private depthOf point =
        let _, _, z = rotate point
        z

    let private project point =
        let x, y, _ = rotate point
        x + stage / 2.0, y + stage / 2.0

    let private matrix (panel: Panel) =
        let ox, oy = project (panel.At 0.0 0.0)
        let ax, ay = project (panel.At panel.Across 0.0)
        let dx, dy = project (panel.At 0.0 panel.Down)

        [ Svg.n ((ax - ox) / panel.Across)
          Svg.n ((ay - oy) / panel.Across)
          Svg.n ((dx - ox) / panel.Down)
          Svg.n ((dy - oy) / panel.Down)
          Svg.n ox
          Svg.n oy ]
        |> String.concat " "

    let private stockDefs (panels: Panel list) (palette: Palette) =
        panels
        |> List.map (fun panel ->
            Svg.linear
                $"stock-{panel.Name.Token}"
                palette.StockAngle
                panel.Across
                panel.Down
                palette.Stock)
        |> String.concat ""

    let private surfaceDefs (palette: Palette) =
        [ """<linearGradient id="carton-form" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff" stop-opacity="0.07"/><stop offset="0.3" stop-color="#000000" stop-opacity="0"/><stop offset="1" stop-color="#000000" stop-opacity="0.16"/></linearGradient>"""
          """<radialGradient id="ground"><stop offset="0" stop-color="#0a0802" stop-opacity="0.55"/><stop offset="0.68" stop-color="#0a0802" stop-opacity="0"/></radialGradient>"""
          """<filter id="soften"><feGaussianBlur stdDeviation="7"/></filter>"""
          if palette.Fibre then
              Svg.stripes "fibre-warp" 72.0 4.0 1.0 "#5a3c14" 0.14
              + Svg.stripes "fibre-weft" -64.0 6.0 1.0 "#5a3c14" 0.10 ]
        |> String.concat ""

    let private grain (panel: Panel) (palette: Palette) =
        if palette.Fibre then
            let warp = Svg.rect 0.0 0.0 panel.Across panel.Down (Svg.url "fibre-warp")
            let weft = Svg.rect 0.0 0.0 panel.Across panel.Down (Svg.url "fibre-weft")
            $"""<g style="mix-blend-mode:multiply" opacity="0.5">{warp}{weft}</g>"""
        else
            ""

    let private shade (panel: Panel) =
        match panel.Shade with
        | Some colour ->
            Svg.rectWith
                0.0
                0.0
                panel.Across
                panel.Down
                colour
                $" fill-opacity=\"{Svg.n panel.ShadeAlpha}\""
        | None -> ""

    let private spine (panel: Panel) (pack: Pack) (profile: PackProfile) (palette: Palette) =
        let printed =
            Svg.esc ($"{profile.Noun} \u00b7 {profile.Name pack.Key}".ToUpperInvariant())

        $"""<text transform="translate({Svg.n (panel.Across / 2.0)} {Svg.n (panel.Down / 2.0)}) rotate(90)" y="6.3" text-anchor="middle" font-family="system-ui,'Segoe UI',sans-serif" font-size="18" font-weight="900" letter-spacing="5.76" fill="{palette.Print}" opacity="0.9">{printed}</text>"""

    let private furniture (panel: Panel) (pack: Pack) (profile: PackProfile) (palette: Palette) =
        match panel.Name with
        | PanelName.Front -> PrintedFace.print pack profile palette FaceMetrics.cartonFront
        | PanelName.Side -> spine panel pack profile palette
        | _ -> ""

    // Every fold shows the pale board core along its edge, which is what tells a carton from a
    // flat panel once the sheen is gone.
    let private core (panel: Panel) =
        match panel.Name with
        | PanelName.Front ->
            Svg.rectWith (panel.Across - 1.0) 0.0 1.0 panel.Down "#ffffff" " fill-opacity=\"0.34\""
            + Svg.rectWith 0.0 0.0 panel.Across 1.0 "#ffffff" " fill-opacity=\"0.3\""
        | PanelName.Side -> Svg.rectWith 0.0 0.0 1.0 panel.Down "#ffffff" " fill-opacity=\"0.18\""
        | _ -> ""

    // The cut board core runs around the whole opening, which is what reads as an open tray
    // rather than a shorter closed box.
    let private rim (panel: Panel) =
        match panel.Name with
        | PanelName.TrayFront ->
            Svg.rectWith 0.0 0.0 panel.Across 1.4 "#ffffff" " fill-opacity=\"0.34\""
            + Svg.rectWith (panel.Across - 1.0) 0.0 1.0 panel.Down "#ffffff" " fill-opacity=\"0.3\""
        | PanelName.TraySide ->
            Svg.rectWith 0.0 0.0 panel.Across 1.4 "#ffffff" " fill-opacity=\"0.26\""
            + Svg.rectWith 0.0 0.0 1.0 panel.Down "#ffffff" " fill-opacity=\"0.18\""
        | PanelName.TrayBack ->
            Svg.rectWith 0.0 0.0 panel.Across 1.4 "#fffcf0" " fill-opacity=\"0.2\""
        | PanelName.TrayLeft ->
            Svg.rectWith 0.0 0.0 panel.Across 1.4 "#fffcf0" " fill-opacity=\"0.16\""
        | _ -> ""

    let private cast =
        """<g transform="translate(325 516) skewX(-27) scale(1 0.48) translate(-325 -516)" filter="url(#soften)"><ellipse cx="325" cy="516" rx="215" ry="46" fill="url(#ground)"/></g>"""

    let private draped (panel: Panel) (content: string) =
        $"""<g transform="matrix({matrix panel})">{content}</g>"""

    // A carton is opaque, so only the faces turned towards the viewer are drawn, and those are
    // painted far to near so the shared folds meet without seams.
    let private turnedTowardsViewer (panels: Panel list) =
        panels
        |> List.filter (fun panel -> depthOf panel.Normal > 0.0)
        |> List.sortBy (fun panel -> depthOf panel.Centre)

    let private face (panel: Panel) (pack: Pack) (profile: PackProfile) (palette: Palette) =
        [ Svg.rect 0.0 0.0 panel.Across panel.Down (Svg.url $"stock-{panel.Name.Token}")
          grain panel palette
          Svg.rect 0.0 0.0 panel.Across panel.Down (Svg.url "carton-form")
          furniture panel pack profile palette
          shade panel
          core panel ]
        |> String.concat ""
        |> draped panel

    let private trayFace (panel: Panel) (palette: Palette) =
        [ Svg.rect 0.0 0.0 panel.Across panel.Down (Svg.url $"stock-{panel.Name.Token}")
          grain panel palette
          Svg.rect 0.0 0.0 panel.Across panel.Down (Svg.url "carton-form")
          shade panel
          rim panel ]
        |> String.concat ""
        |> draped panel

    /// Draws a carton.
    let Draw (pack: Pack) (profile: PackProfile) =
        let palette = Palette.forMaterial (pack.MaterialUnder profile.Stock)

        let art =
            faces
            |> turnedTowardsViewer
            |> List.map (fun panel -> face panel pack profile palette)
            |> String.concat ""

        Document.wrap
            { X = 0.0
              Y = 0.0
              Width = stage
              Height = stage
              Title = $"{profile.Noun} {profile.Name pack.Key} carton"
              Defs = stockDefs faces palette + surfaceDefs palette + PrintedFace.defs
              Art = cast + art
              Style = ""
              Scope = $"{PackKey.slug pack.Key}-{profile.Stock.ToString().ToLowerInvariant()}" }

    /// Draws the open inner tray the carton's lid slides off.
    let DrawTray (pack: Pack) (profile: PackProfile) =
        let palette = Palette.forMaterial (pack.MaterialUnder profile.Stock)

        let art =
            trayFaces
            |> turnedTowardsViewer
            |> List.map (fun panel -> trayFace panel palette)
            |> String.concat ""

        Document.wrap
            { X = 0.0
              Y = 0.0
              Width = stage
              Height = stage
              Title = $"{profile.Noun} {profile.Name pack.Key} inner tray"
              Defs = stockDefs trayFaces palette + surfaceDefs palette
              Art = cast + art
              Style = ""
              Scope = $"{PackKey.slug pack.Key}-tray-{profile.Stock.ToString().ToLowerInvariant()}" }
