import QtQuick
import QtQuick.Controls.Basic

AbstractButton {
    id: control

    property color tint: theme.accent
    property bool lit: checked

    hoverEnabled: true
    font.family: theme.font
    font.pixelSize: 12
    implicitHeight: 28
    implicitWidth: label.implicitWidth + 24

    contentItem: Text {
        id: label
        text: control.text
        font: control.font
        horizontalAlignment: Text.AlignHCenter
        verticalAlignment: Text.AlignVCenter
        color: !control.enabled ? theme.faint : control.lit ? control.tint : theme.text
    }

    background: Rectangle {
        radius: height / 2
        color: control.lit ? theme.alpha(control.tint, 0.16)
             : control.hovered && control.enabled ? theme.alpha(control.tint, 0.08) : theme.raised
        border.width: 1
        border.color: !control.enabled ? theme.alpha(control.tint, 0.12)
                    : control.lit ? control.tint : theme.alpha(control.tint, 0.32)
    }
}
