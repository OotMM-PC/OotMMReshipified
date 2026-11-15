import QtQuick

Rectangle {
    id: pill

    property string text
    property bool ok: false

    width: label.implicitWidth + 14
    height: 20
    radius: 10
    color: ok ? theme.alpha(theme.success, 0.15) : theme.raised
    border.width: 1
    border.color: ok ? theme.alpha(theme.success, 0.5) : theme.alpha(theme.accent, 0.2)

    Text {
        id: label
        anchors.centerIn: parent
        text: (pill.ok ? "✓ " : "") + pill.text
        color: pill.ok ? theme.success : theme.dim
        font.family: theme.font
        font.pixelSize: 11
    }
}
