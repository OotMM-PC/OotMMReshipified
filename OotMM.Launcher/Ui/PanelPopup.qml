import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

Popup {
    id: popup

    property string title: ""
    property color tint: theme.accent
    default property alias content: body.data

    modal: true
    focus: true
    padding: 18
    closePolicy: Popup.CloseOnEscape | Popup.CloseOnPressOutside
    anchors.centerIn: Overlay.overlay
    width: Math.min(520, parent ? parent.width - 48 : 520)

    Overlay.modal: Rectangle {
        color: theme.alpha(theme.bgDeep, 0.72)
    }

    background: BracketFrame {
        bracketColor: popup.tint
        fill: theme.raised
        padding: 0
    }

    contentItem: ColumnLayout {
        spacing: 12

        RowLayout {
            Layout.fillWidth: true

            Text {
                Layout.fillWidth: true
                text: popup.title
                color: popup.tint
                font.family: theme.font
                font.pixelSize: 17
                font.weight: Font.DemiBold
            }

            LinkButton {
                text: "Close"
                tint: popup.tint
                onClicked: popup.close()
            }
        }

        ColumnLayout {
            id: body
            Layout.fillWidth: true
            spacing: 8
        }
    }
}
