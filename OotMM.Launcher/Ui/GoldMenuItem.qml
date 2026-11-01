import QtQuick
import QtQuick.Controls.Basic

MenuItem {
    id: control

    implicitHeight: 32
    font.family: theme.font
    font.pixelSize: 13
    hoverEnabled: true

    contentItem: Text {
        leftPadding: 4
        text: control.text
        font: control.font
        color: control.enabled ? theme.text : theme.faint
        verticalAlignment: Text.AlignVCenter
    }

    background: Rectangle {
        implicitWidth: 220
        color: control.highlighted || control.hovered ? theme.alpha(theme.accent, 0.16) : "transparent"
        radius: 4
    }
}
