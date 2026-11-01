import QtQuick
import QtQuick.Controls.Basic

AbstractButton {
    id: control

    property color tint: theme.accent

    hoverEnabled: true
    font.family: theme.font
    font.pixelSize: 13
    implicitWidth: label.implicitWidth
    implicitHeight: label.implicitHeight

    contentItem: Text {
        id: label
        text: control.text
        font.family: control.font.family
        font.pixelSize: control.font.pixelSize
        font.underline: control.hovered && control.enabled
        color: control.enabled ? control.tint : theme.faint
        verticalAlignment: Text.AlignVCenter
    }

    background: null
}
