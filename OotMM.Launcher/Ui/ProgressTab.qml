import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

ColumnLayout {
    id: tab

    property var vm

    function open(section) {
        tab.vm.openSection(section)
        list.contentY = 0
        dungeons.contentY = 0
    }

    spacing: 8

    Row {
        id: sections
        Layout.fillWidth: true
        spacing: 4

        Repeater {
            id: sectionChips
            model: tab.vm ? tab.vm.sections : []

            delegate: Chip {
                required property var modelData
                width: (sections.width - sections.spacing * (sectionChips.count - 1)) / Math.max(1, sectionChips.count)
                text: modelData.label
                font.pixelSize: 11
                lit: !!(tab.vm && tab.vm.sectionIndex === modelData.key)
                onClicked: tab.open(modelData.key)
            }
        }
    }

    RowLayout {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.showList)
        spacing: 6

        Chip {
            text: "Ocarina of Time"
            tint: theme.gold
            lit: !!(tab.vm && tab.vm.gameIndex === 0)
            onClicked: {
                tab.vm.gameIndex = 0
                list.contentY = 0
            }
        }

        Chip {
            text: "Majora's Mask"
            tint: theme.mm
            lit: !!(tab.vm && tab.vm.gameIndex === 1)
            onClicked: {
                tab.vm.gameIndex = 1
                list.contentY = 0
            }
        }

        Item {
            Layout.fillWidth: true
        }

        GoldCheck {
            visible: !!(tab.vm && tab.vm.showMissingOnly)
            text: "Missing only"
            wrap: false
            on: !!(tab.vm && tab.vm.missingOnly)
            ToolTip.visible: hovered
            ToolTip.delay: 500
            ToolTip.text: "Hide everything you have already found all of."
            onFlipped: value => {
                tab.vm.missingOnly = value
                list.contentY = 0
            }
        }
    }

    Field {
        id: foundSearch
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.showFoundFilter)
        rightPadding: clearFound.visible ? clearFound.width + 12 : 9
        placeholderText: "Search items, players or checks"
        text: tab.vm ? tab.vm.foundFilter : ""
        onTextEdited: {
            tab.vm.foundFilter = text
            list.contentY = 0
        }
        onAccepted: focus = false
        Keys.onEscapePressed: {
            tab.vm.foundFilter = ""
            focus = false
        }

        LinkButton {
            id: clearFound
            anchors.right: parent.right
            anchors.rightMargin: 9
            anchors.verticalCenter: parent.verticalCenter
            visible: foundSearch.text.length > 0
            text: "✕"
            ToolTip.visible: hovered
            ToolTip.delay: 500
            ToolTip.text: "Clear the search"
            onClicked: {
                tab.vm.foundFilter = ""
                foundSearch.forceActiveFocus()
            }
        }
    }

    Rectangle {
        Layout.fillWidth: true
        height: 1
        color: theme.alpha(theme.accent, 0.2)
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
            spacing: 2

            SectionLabel {
                text: "Checks"
            }

            Repeater {
                model: tab.vm ? tab.vm.checkMeters : []

                delegate: Meter {
                    required property var modelData
                    Layout.fillWidth: true
                    label: modelData.label
                    count: modelData.countText
                    fraction: modelData.fraction
                    done: modelData.done
                }
            }

            SectionLabel {
                Layout.topMargin: 12
                text: "Goals"
            }

            Text {
                Layout.fillWidth: true
                visible: text.length > 0
                wrapMode: Text.WordWrap
                text: tab.vm ? tab.vm.goalText : ""
                color: theme.text
                font.family: theme.font
                font.pixelSize: 13
            }

            Flow {
                Layout.fillWidth: true
                Layout.topMargin: 4
                Layout.bottomMargin: 4
                spacing: 4

                Repeater {
                    model: tab.vm ? tab.vm.bosses : []

                    delegate: StatusPill {
                        required property var modelData
                        text: modelData.text
                        ok: modelData.ok
                    }
                }
            }

            Repeater {
                model: tab.vm ? tab.vm.goals : []

                delegate: ColumnLayout {
                    id: goal
                    required property var modelData
                    Layout.fillWidth: true
                    spacing: 0

                    Meter {
                        Layout.fillWidth: true
                        label: goal.modelData.name
                        count: goal.modelData.countText
                        fraction: goal.modelData.fraction
                        done: goal.modelData.satisfied
                    }

                    Text {
                        Layout.fillWidth: true
                        Layout.bottomMargin: 4
                        visible: text.length > 0
                        wrapMode: Text.WordWrap
                        text: goal.modelData.detail
                        color: theme.dim
                        font.family: theme.font
                        font.pixelSize: 11
                    }
                }
            }

            Text {
                Layout.fillWidth: true
                visible: !!(tab.vm && !tab.vm.hasGoals)
                wrapMode: Text.WordWrap
                text: "This seed sets no item requirements for the bridge, the moon or the final bosses."
                color: theme.dim
                font.family: theme.font
                font.pixelSize: 12
            }

            SectionLabel {
                Layout.topMargin: 12
                text: "Collection"
            }

            Repeater {
                model: tab.vm ? tab.vm.collection : []

                delegate: Meter {
                    required property var modelData
                    Layout.fillWidth: true
                    clickable: true
                    label: modelData.label
                    count: modelData.countText
                    fraction: modelData.fraction
                    done: modelData.done
                    onClicked: tab.open(modelData.section)
                }
            }
        }
    }

    Flickable {
        id: dungeons
        Layout.fillWidth: true
        Layout.fillHeight: true
        visible: !!(tab.vm && tab.vm.showDungeons)
        clip: true
        contentHeight: dungeonColumn.implicitHeight
        boundsBehavior: Flickable.StopAtBounds
        ScrollBar.vertical: ScrollBar { }

        Column {
            id: dungeonColumn
            width: dungeons.width - 10

            Text {
                width: parent.width
                wrapMode: Text.WordWrap
                text: "A ✓ marks what you have found. Checks count everything shuffled inside the dungeon."
                color: theme.dim
                font.family: theme.font
                font.pixelSize: 11
            }

            Repeater {
                model: tab.vm ? tab.vm.dungeons : []

                delegate: Column {
                    id: dungeon
                    required property var modelData
                    width: dungeonColumn.width
                    topPadding: 10
                    spacing: 5

                    SectionLabel {
                        visible: dungeon.modelData.isHeader
                        text: dungeon.modelData.name
                    }

                    RowLayout {
                        visible: !dungeon.modelData.isHeader
                        width: parent.width
                        spacing: 6

                        Text {
                            Layout.fillWidth: true
                            elide: Text.ElideRight
                            text: dungeon.modelData.name
                            color: dungeon.modelData.done ? theme.success : theme.text
                            font.family: theme.font
                            font.pixelSize: 13
                        }

                        Text {
                            visible: dungeon.modelData.checksText.length > 0
                            text: (dungeon.modelData.done ? "✓ " : "") + dungeon.modelData.checksText + " checks"
                            color: dungeon.modelData.done ? theme.success : theme.dim
                            font.family: theme.font
                            font.pixelSize: 12
                        }
                    }

                    Flow {
                        visible: !dungeon.modelData.isHeader && pills.count > 0
                        width: parent.width
                        spacing: 4

                        Repeater {
                            id: pills
                            model: dungeon.modelData.chips

                            delegate: StatusPill {
                                required property var modelData
                                text: modelData.text
                                ok: modelData.ok
                            }
                        }
                    }
                }
            }
        }
    }

    Text {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.showList && !tab.vm.hasRows)
        wrapMode: Text.WordWrap
        text: tab.vm ? tab.vm.emptyText : ""
        color: theme.faint
        font.family: theme.font
        font.pixelSize: 12
    }

    Flickable {
        id: list
        Layout.fillWidth: true
        Layout.fillHeight: true
        visible: !!(tab.vm && tab.vm.showList)
        clip: true
        contentHeight: listColumn.implicitHeight
        boundsBehavior: Flickable.StopAtBounds
        ScrollBar.vertical: ScrollBar { }

        Column {
            id: listColumn
            width: list.width - 10

            Repeater {
                model: tab.vm ? tab.vm.rows : []

                delegate: Item {
                    id: row
                    required property var modelData
                    required property int index
                    readonly property bool header: modelData.isHeader
                    readonly property bool twoLine: !header && modelData.detail.length > 0
                    readonly property color tone: modelData.level === 2 ? theme.success
                                                : modelData.level === 1 ? theme.warning : theme.dim
                    width: listColumn.width
                    height: header ? (index === 0 ? 26 : 40) : twoLine ? 38 : 22

                    RowLayout {
                        anchors.left: parent.left
                        anchors.right: parent.right
                        anchors.bottom: parent.bottom
                        anchors.bottomMargin: row.twoLine ? 16 : 0
                        height: row.header ? 26 : 22
                        spacing: 8

                        Text {
                            visible: !row.header
                            Layout.preferredWidth: 12
                            horizontalAlignment: Text.AlignHCenter
                            text: row.modelData.level === 2 ? "✓" : "○"
                            color: row.tone
                            font.family: theme.font
                            font.pixelSize: 12
                        }

                        Text {
                            Layout.fillWidth: true
                            elide: Text.ElideRight
                            textFormat: Text.PlainText
                            text: row.modelData.text
                            color: row.header ? theme.accentBright
                                 : row.twoLine ? theme.text
                                 : row.modelData.level === 2 ? theme.success
                                 : row.modelData.level === 1 ? theme.text : theme.dim
                            font.family: theme.font
                            font.pixelSize: 13
                            font.weight: row.header ? Font.DemiBold : Font.Normal
                        }

                        Text {
                            text: row.modelData.countText
                            color: row.header ? (row.modelData.level === 2 ? theme.success : theme.dim) : row.tone
                            font.family: theme.font
                            font.pixelSize: 12
                        }
                    }

                    Text {
                        visible: row.twoLine
                        anchors.left: parent.left
                        anchors.leftMargin: 20
                        anchors.right: parent.right
                        anchors.bottom: parent.bottom
                        anchors.bottomMargin: 2
                        elide: Text.ElideRight
                        textFormat: Text.PlainText
                        text: row.modelData.detail
                        color: theme.dim
                        font.family: theme.font
                        font.pixelSize: 11
                    }

                    Rectangle {
                        visible: row.header
                        anchors.bottom: parent.bottom
                        width: parent.width
                        height: 1
                        color: theme.alpha(theme.accent, 0.2)
                    }
                }
            }
        }
    }
}
