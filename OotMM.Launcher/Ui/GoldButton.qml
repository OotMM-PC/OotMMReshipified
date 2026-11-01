import QtQuick
import QtQuick.Controls.Basic

Button {
    id: control

    property color tint: theme.accent
    property bool danger: false

    implicitHeight: 34
    leftPadding: 14
    rightPadding: 14
    font.family: theme.font
    font.pixelSize: 13
    hoverEnabled: true

    contentItem: Text {
        text: control.text
        font: control.font
        color: !control.enabled ? theme.faint : control.danger ? theme.error : theme.text
        horizontalAlignment: Text.AlignLeft
        verticalAlignment: Text.AlignVCenter
        elide: Text.ElideRight
    }

    background: Rectangle {
        radius: 6
        color: control.down ? theme.alpha(control.tint, 0.22)
             : control.hovered ? theme.alpha(control.tint, 0.12) : theme.raised
        border.width: 1
        border.color: !control.enabled ? theme.alpha(control.tint, 0.15)
                    : control.danger ? theme.alpha(theme.error, 0.6)
                    : control.hovered ? control.tint : theme.alpha(control.tint, 0.4)
    }
}
