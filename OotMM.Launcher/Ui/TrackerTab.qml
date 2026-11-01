import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

ColumnLayout {
    id: tab

    property var vm

    spacing: 8

    Rectangle {
        Layout.fillWidth: true
        implicitHeight: 24
        radius: 6
        color: theme.bgDeep
        border.width: 1
        border.color: theme.alpha(theme.accent, 0.35)
        clip: true

        Rectangle {
            width: Math.max(0, (parent.width - 2) * (tab.vm ? tab.vm.collectedFraction : 0))
            height: parent.height - 2
            x: 1
            y: 1
            radius: 5
            color: theme.alpha(theme.accent, 0.75)
        }

        Text {
            anchors.centerIn: parent
            text: tab.vm ? tab.vm.summaryText : ""
            color: theme.text
            style: Text.Outline
            styleColor: theme.bgDeep
            font.family: theme.font
            font.pixelSize: 12
            font.weight: Font.DemiBold
        }
    }

    Text {
        Layout.fillWidth: true
        wrapMode: Text.WordWrap
        text: tab.vm ? tab.vm.inLogicText : ""
        color: theme.success
        font.family: theme.font
        font.pixelSize: 12
    }

    Text {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.showStuckNote)
        wrapMode: Text.WordWrap
        text: tab.vm ? tab.vm.stuckNote : ""
        color: theme.warning
        font.family: theme.font
        font.pixelSize: 12
    }

    Field {
        Layout.fillWidth: true
        placeholderText: "filter checks…"
        text: tab.vm ? tab.vm.filterText : ""
        onTextEdited: tab.vm.filterText = text
    }

    RowLayout {
        Layout.fillWidth: true
        spacing: 10

        GoldCombo {
            Layout.preferredWidth: 140
            model: ["Both games", "Ocarina of Time", "Majora's Mask"]
            currentIndex: tab.vm ? tab.vm.gameFilterIndex : 0
            onActivated: index => tab.vm.gameFilterIndex = index
        }

        GoldCheck {
            text: "Hide collected"
            checked: tab.vm ? tab.vm.hideCollected : true
            onToggled: tab.vm.hideCollected = checked
        }

        GoldCheck {
            text: "In logic"
            checked: tab.vm ? tab.vm.onlyInLogic : true
            onToggled: tab.vm.onlyInLogic = checked
        }
    }

    GoldCheck {
        visible: !!(tab.vm && tab.vm.devMode)
        text: "Force-unlock mode"
        tint: theme.error
        checked: tab.vm ? tab.vm.forceUnlockMode : false
        onToggled: tab.vm.forceUnlockMode = checked
    }

    Text {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.forceUnlockMode)
        wrapMode: Text.WordWrap
        text: "Cheat active: click an open check, then click again to confirm."
        color: theme.error
        font.family: theme.font
        font.pixelSize: 12
    }

    Rectangle {
        Layout.fillWidth: true
        height: 1
        color: theme.alpha(theme.accent, 0.2)
    }

    ListView {
        id: list
        Layout.fillWidth: true
        Layout.fillHeight: true
        clip: true
        model: tab.vm ? tab.vm.visibleChecks : []
        ScrollBar.vertical: ScrollBar { }

        delegate: Item {
            id: row
            required property var modelData
            width: list.width - 10
            height: body.implicitHeight + 12

            Rectangle {
                anchors.fill: parent
                color: row.modelData.isArmed ? theme.alpha(theme.error, 0.15)
                     : hover.hovered && tab.vm.forceUnlockMode ? theme.alpha(theme.accent, 0.08) : "transparent"
            }

            Rectangle {
                anchors.bottom: parent.bottom
                width: parent.width
                height: 1
                color: theme.alpha(theme.accent, 0.1)
            }

            RowLayout {
                id: body
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                spacing: 6

                Text {
                    Layout.alignment: Qt.AlignTop
                    text: "•"
                    color: theme.faint
                    font.pixelSize: 13
                }

                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: 1

                    Text {
                        Layout.fillWidth: true
                        wrapMode: Text.WordWrap
                        textFormat: Text.StyledText
                        text: "<font color='" + (row.modelData.gameTag === "MM" ? theme.mmBright : theme.goldBright)
                              + "'>[" + row.modelData.gameTag + "]</font> " + row.modelData.displayName
                        color: row.modelData.collected ? theme.faint : theme.text
                        font.family: theme.font
                        font.pixelSize: 13
                        font.strikeout: row.modelData.collected
                    }

                    Text {
                        Layout.fillWidth: true
                        visible: text.length > 0
                        wrapMode: Text.WordWrap
                        text: row.modelData.itemText
                        color: theme.dim
                        font.family: theme.font
                        font.pixelSize: 12
                    }
                }
            }

            HoverHandler {
                id: hover
            }

            TapHandler {
                onTapped: row.modelData.click()
            }
        }
    }
}
