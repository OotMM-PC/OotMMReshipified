import QtQuick

QtObject {
    property bool violet: false

    readonly property string font: "Segoe UI"
    readonly property string mono: "Consolas"
    readonly property color bgDeep: "#0E0B07"
    readonly property color panel: "#191309"
    readonly property color raised: "#221A0D"
    readonly property color field: "#2A2014"
    readonly property color text: "#E8DCC2"
    readonly property color dim: "#9C9077"
    readonly property color faint: "#6F6553"
    readonly property color success: "#8CD980"
    readonly property color error: "#EB6657"
    readonly property color warning: "#F2BF40"
    readonly property color gold: "#E0B84C"
    readonly property color goldBright: "#F2D27C"
    readonly property color goldInk: "#1B1406"
    readonly property color mm: "#A97FE0"
    readonly property color mmBright: "#C7A6F2"
    readonly property color mmInk: "#170D26"
    readonly property color accent: violet ? mm : gold
    readonly property color accentBright: violet ? mmBright : goldBright
    readonly property color accentInk: violet ? mmInk : goldInk

    function alpha(c, a) {
        return Qt.rgba(c.r, c.g, c.b, a)
    }

    function icon(name) {
        return "qrc:/assemblies/OotMM.Launcher/Assets/Icons/" + name + ".png"
    }

    function localPath(url) {
        var s = url.toString()
        if (s.startsWith("file:///"))
            s = s.substring(8)
        return decodeURIComponent(s).replace(/\//g, "\\")
    }
}
