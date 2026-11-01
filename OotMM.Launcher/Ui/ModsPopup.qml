import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

PanelPopup {
    id: popup

    property var card

    title: card ? card.title + " mods" : "Mods"
    tint: card && card.isOot ? theme.gold : theme.mm

    Text {
        Layout.fillWidth: true
        wrapMode: Text.WordWrap
        text: "The top mod overrides everything below it. Changes apply on the next game launch. Click names to select several, then move them together."
        color: theme.dim
        font.family: theme.font
        font.pixelSize: 12
    }

    Row {
        spacing: 6

        Repeater {
            model: [
                { label: "Top", act: "top" },
                { label: "Up", act: "up" },
                { label: "Down", act: "down" },
                { label: "Bottom", act: "bottom" }
            ]

            delegate: Chip {
                required property var modelData
                text: modelData.label
                tint: popup.tint
                onClicked: {
                    if (modelData.act === "top") popup.card.moveSelectedTop()
                    else if (modelData.act === "up") popup.card.moveSelectedUp()
                    else if (modelData.act === "down") popup.card.moveSelectedDown()
                    else popup.card.moveSelectedBottom()
                }
            }
        }
    }

    ListView {
        id: list
        Layout.fillWidth: true
        Layout.preferredHeight: Math.min(contentHeight, 320)
        clip: true
        spacing: 2
        model: popup.card ? popup.card.mods : []
        ScrollBar.vertical: ScrollBar { policy: list.contentHeight > list.height ? ScrollBar.AlwaysOn : ScrollBar.AsNeeded }

        delegate: Rectangle {
            id: row
            required property var modelData
            width: list.width - 12
            height: 32
            radius: 5
            color: modelData.isSelected ? theme.alpha(popup.tint, 0.18) : theme.panel
            border.width: 1
            border.color: modelData.isSelected ? popup.tint : "transparent"

            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 6
                anchors.rightMargin: 8
                spacing: 6

                LinkButton {
                    text: "▲"
                    tint: popup.tint
                    enabled: !row.modelData.isFirst
                    onClicked: row.modelData.moveUp()
                }

                LinkButton {
                    text: "▼"
                    tint: popup.tint
                    enabled: !row.modelData.isLast
                    onClicked: row.modelData.moveDown()
                }

                GoldCheck {
                    tint: popup.tint
                    checked: row.modelData.enabled
                    enabled: !row.modelData.required
                    onToggled: row.modelData.enabled = checked
                }

                Text {
                    Layout.fillWidth: true
                    text: row.modelData.name + (row.modelData.required ? "  (required)" : "")
                    color: row.modelData.required ? theme.dim : theme.text
                    elide: Text.ElideMiddle
                    font.family: theme.font
                    font.pixelSize: 13

                    MouseArea {
                        anchors.fill: parent
                        onClicked: row.modelData.isSelected = !row.modelData.isSelected
                    }
                }
            }
        }
    }

    Text {
        Layout.fillWidth: true
        visible: !!(popup.card && popup.card.hasLoadOrderError)
        wrapMode: Text.WordWrap
        text: popup.card ? popup.card.loadOrderError : ""
        color: theme.error
        font.family: theme.font
        font.pixelSize: 12
    }
}
