import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

ColumnLayout {
    id: tab

    property var vm

    spacing: 8

    RowLayout {
        Layout.fillWidth: true
        spacing: 8

        Text {
            text: "Category"
            color: theme.text
            font.family: theme.font
            font.pixelSize: 13
        }

        GoldCombo {
            Layout.fillWidth: true
            model: ["Overview", "Dungeons", "Souls", "Songs", "Key items", "Rusty keys"]
            currentIndex: tab.vm ? tab.vm.categoryIndex : 0
            onActivated: index => tab.vm.categoryIndex = index
        }
    }

    Row {
        visible: !!(tab.vm && tab.vm.showGameRadio)
        spacing: 6

        Chip {
            text: "Ocarina of Time"
            tint: theme.gold
            lit: !!(tab.vm && tab.vm.gameIndex === 0)
            onClicked: tab.vm.gameIndex = 0
        }

        Chip {
            text: "Majora's Mask"
            tint: theme.mm
            lit: !!(tab.vm && tab.vm.gameIndex === 1)
            onClicked: tab.vm.gameIndex = 1
        }
    }

    Text {
        visible: !!(tab.vm && tab.vm.showSongsMode)
        text: tab.vm ? tab.vm.songsModeText : ""
        color: theme.dim
        font.family: theme.font
        font.pixelSize: 12
    }

    Flickable {
        id: overview
        Layout.fillWidth: true
        Layout.fillHeight: true
        visible: !!(tab.vm && tab.vm.showOverview)
        clip: true
        contentHeight: overviewColumn.implicitHeight
        boundsBehavior: Flickable.StopAtBounds
        ScrollBar.vertical: ScrollBar { }

        ColumnLayout {
            id: overviewColumn
            width: overview.width - 10
            spacing: 4

            SectionLabel {
                text: "Checks"
            }

            Text {
                text: tab.vm ? tab.vm.checksText : ""
                color: theme.text
                font.family: theme.font
                font.pixelSize: 13
            }

            Text {
                text: tab.vm ? tab.vm.itemsOwnedText : ""
                color: theme.text
                font.family: theme.font
                font.pixelSize: 13
            }

            Text {
                text: tab.vm ? tab.vm.ootChecksText : ""
                color: theme.goldBright
                font.family: theme.font
                font.pixelSize: 13
            }

            Text {
                text: tab.vm ? tab.vm.mmChecksText : ""
                color: theme.mmBright
                font.family: theme.font
                font.pixelSize: 13
            }

            Text {
                visible: !!(tab.vm && tab.vm.showMultiworldText)
                text: tab.vm ? tab.vm.multiworldText : ""
                color: theme.dim
                font.family: theme.font
                font.pixelSize: 12
            }

            SectionLabel {
                Layout.topMargin: 8
                text: "Goals"
            }

            Repeater {
                model: tab.vm ? tab.vm.goals : []

                delegate: Text {
                    required property var modelData
                    Layout.fillWidth: true
                    wrapMode: Text.WordWrap
                    text: modelData.text
                    color: modelData.satisfied ? theme.success : modelData.open ? theme.error : theme.faint
                    font.family: theme.font
                    font.pixelSize: 13
                }
            }

            Text {
                visible: !!(tab.vm && !tab.vm.hasGoals)
                text: "No goal data in this seed."
                color: theme.faint
                font.family: theme.font
                font.pixelSize: 12
            }

            Text {
                Layout.fillWidth: true
                Layout.topMargin: 4
                wrapMode: Text.WordWrap
                text: tab.vm ? tab.vm.goalsNote : ""
                color: theme.dim
                font.family: theme.font
                font.pixelSize: 11
            }
        }
    }

    ListView {
        id: dungeons
        Layout.fillWidth: true
        Layout.fillHeight: true
        visible: !!(tab.vm && tab.vm.showDungeons)
        clip: true
        spacing: 6
        model: tab.vm ? tab.vm.dungeons : []
        ScrollBar.vertical: ScrollBar { }

        delegate: Column {
            id: dungeon
            required property var modelData
            width: dungeons.width - 10
            spacing: 4

            Text {
                textFormat: Text.StyledText
                text: "<font color='" + (dungeon.modelData.gameTag === "MM" ? theme.mmBright : theme.goldBright)
                      + "'>[" + dungeon.modelData.gameTag + "]</font> " + dungeon.modelData.name
                color: theme.text
                font.family: theme.font
                font.pixelSize: 13
            }

            Flow {
                width: parent.width
                spacing: 4

                Repeater {
                    model: dungeon.modelData.chips

                    delegate: Rectangle {
                        required property var modelData
                        width: chipText.implicitWidth + 12
                        height: 20
                        radius: 10
                        color: modelData.ok ? theme.alpha(theme.success, 0.15) : theme.raised
                        border.width: 1
                        border.color: modelData.ok ? theme.alpha(theme.success, 0.5) : theme.alpha(theme.accent, 0.2)

                        Text {
                            id: chipText
                            anchors.centerIn: parent
                            text: parent.modelData.text
                            color: parent.modelData.ok ? theme.success : theme.dim
                            font.family: theme.font
                            font.pixelSize: 11
                        }
                    }
                }
            }
        }
    }

    ListView {
        id: souls
        Layout.fillWidth: true
        Layout.fillHeight: true
        visible: !!(tab.vm && tab.vm.showSouls)
        clip: true
        spacing: 8
        model: tab.vm ? tab.vm.soulGroups : []
        ScrollBar.vertical: ScrollBar { }

        delegate: Column {
            id: group
            required property var modelData
            width: souls.width - 10
            spacing: 3

            SectionLabel {
                text: group.modelData.title
                font.pixelSize: 13
            }

            Repeater {
                model: group.modelData.items

                delegate: Text {
                    required property var modelData
                    text: modelData.mark + "  " + modelData.name
                    color: modelData.owned ? theme.success : theme.faint
                    font.family: theme.font
                    font.pixelSize: 12
                }
            }
        }
    }

    Text {
        visible: !!(tab.vm && tab.vm.showItemList)
        text: tab.vm ? tab.vm.itemsHeader : ""
        color: theme.dim
        font.family: theme.font
        font.pixelSize: 12
    }

    ListView {
        id: items
        Layout.fillWidth: true
        Layout.fillHeight: true
        visible: !!(tab.vm && tab.vm.showItemList)
        clip: true
        model: tab.vm ? tab.vm.items : []
        ScrollBar.vertical: ScrollBar { }

        delegate: Text {
            required property var modelData
            width: items.width - 10
            height: 22
            verticalAlignment: Text.AlignVCenter
            elide: Text.ElideRight
            text: modelData.mark + "  " + modelData.name
            color: modelData.owned ? theme.success : theme.faint
            font.family: theme.font
            font.pixelSize: 13
        }
    }
}
