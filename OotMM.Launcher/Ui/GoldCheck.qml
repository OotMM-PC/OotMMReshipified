import QtQuick
import QtQuick.Controls.Basic

CheckBox {
    id: control

    property color tint: theme.accent

    font.family: theme.font
    font.pixelSize: 13
    spacing: 8
    padding: 2
    hoverEnabled: true

    indicator: Rectangle {
        x: control.leftPadding
        y: control.topPadding + (control.availableHeight - height) / 2
        width: 16
        height: 16
        radius: 3
        color: control.checked ? control.tint : theme.field
        border.width: 1
        border.color: control.enabled ? (control.hovered || control.checked ? control.tint : theme.alpha(control.tint, 0.45))
                                      : theme.alpha(control.tint, 0.15)
        opacity: control.enabled ? 1 : 0.5

        Text {
            anchors.centerIn: parent
            text: "✓"
            visible: control.checked
            color: theme.goldInk
            font.pixelSize: 12
            font.bold: true
        }
    }

    contentItem: Text {
        leftPadding: control.indicator.width + control.spacing
        text: control.text
        font: control.font
        color: control.enabled ? theme.text : theme.faint
        verticalAlignment: Text.AlignVCenter
        wrapMode: Text.WordWrap
    }
}
