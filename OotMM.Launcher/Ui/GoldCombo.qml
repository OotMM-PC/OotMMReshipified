import QtQuick
import QtQuick.Controls.Basic

ComboBox {
    id: control

    property color tint: theme.accent

    implicitHeight: 30
    font.family: theme.font
    font.pixelSize: 13

    contentItem: Text {
        leftPadding: 9
        rightPadding: control.indicator.width + 6
        text: control.displayText
        font: control.font
        color: theme.text
        verticalAlignment: Text.AlignVCenter
        elide: Text.ElideRight
    }

    indicator: Text {
        x: control.width - width - 9
        y: (control.height - height) / 2
        text: "▾"
        color: control.tint
        font.pixelSize: 12
    }

    background: Rectangle {
        radius: 5
        color: theme.field
        border.width: 1
        border.color: control.hovered || control.popup.visible ? control.tint : theme.alpha(control.tint, 0.35)
    }

    delegate: ItemDelegate {
        id: entry
        required property int index
        required property var modelData
        width: control.width
        height: 28
        highlighted: control.highlightedIndex === index
        contentItem: Text {
            text: entry.modelData
            font: control.font
            color: entry.highlighted ? control.tint : theme.text
            verticalAlignment: Text.AlignVCenter
        }
        background: Rectangle {
            color: entry.highlighted ? theme.alpha(control.tint, 0.15) : "transparent"
        }
    }

    popup.background: Rectangle {
        color: theme.raised
        radius: 5
        border.width: 1
        border.color: theme.alpha(control.tint, 0.45)
    }
}
