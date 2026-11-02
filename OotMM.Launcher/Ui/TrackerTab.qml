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
        id: search
        Layout.fillWidth: true
        rightPadding: clear.visible ? clear.width + 12 : 9
        placeholderText: "Search checks (Ctrl+F)"
        text: tab.vm ? tab.vm.filterText : ""
        onTextEdited: tab.vm.filterText = text
        onAccepted: focus = false
        Keys.onEscapePressed: {
            tab.vm.filterText = ""
            focus = false
        }

        LinkButton {
            id: clear
            anchors.right: parent.right
            anchors.rightMargin: 9
            anchors.verticalCenter: parent.verticalCenter
            visible: search.text.length > 0
            text: "✕"
            ToolTip.visible: hovered
            ToolTip.delay: 500
            ToolTip.text: "Clear the search"
            onClicked: {
                tab.vm.filterText = ""
                search.forceActiveFocus()
            }
        }
    }

    Shortcut {
        sequences: [StandardKey.Find]
        onActivated: {
            search.forceActiveFocus()
            search.selectAll()
        }
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
            wrap: false
            on: tab.vm ? tab.vm.hideCollected : true
            onFlipped: value => tab.vm.hideCollected = value
        }

        GoldCheck {
            text: "In logic"
            wrap: false
            on: tab.vm ? tab.vm.onlyInLogic : true
            ToolTip.visible: hovered
            ToolTip.delay: 500
            ToolTip.text: "Only checks reachable with your current items, per the seed's logic."
            onFlipped: value => tab.vm.onlyInLogic = value
        }
    }

    GoldCheck {
        visible: !!(tab.vm && tab.vm.devMode)
        text: "Force-unlock mode"
        tint: theme.error
        on: tab.vm ? tab.vm.forceUnlockMode : false
        ToolTip.visible: hovered
        ToolTip.delay: 500
        ToolTip.text: "Cheat: click an open check to arm it, then click again to force-complete it. The running game grants its item through the normal pipeline."
        onFlipped: value => tab.vm.forceUnlockMode = value
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

    RowLayout {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.hiddenMatches > 0)
        spacing: 6

        Text {
            Layout.fillWidth: true
            wrapMode: Text.WordWrap
            text: tab.vm ? tab.vm.hiddenMatchesText : ""
            color: theme.dim
            font.family: theme.font
            font.pixelSize: 12
        }

        LinkButton {
            text: "Show them"
            font.pixelSize: 12
            onClicked: tab.vm.showAllMatches()
        }
    }

    Text {
        Layout.fillWidth: true
        visible: list.count === 0
        wrapMode: Text.WordWrap
        text: tab.vm && tab.vm.filterText.length > 0 ? "No checks match \"" + tab.vm.filterText + "\"."
                                                    : "No checks to show with these filters."
        textFormat: Text.PlainText
        color: theme.faint
        font.family: theme.font
        font.pixelSize: 12
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
            required property int index
            readonly property var check: tab.vm.checkAt(index)
            readonly property bool clickable: !!(tab.vm && tab.vm.forceUnlockMode) && !row.check.collected && !row.check.isPending && row.check.checkId.length > 0
            readonly property color nameColor: row.check.isArmed ? theme.error
                                             : row.check.isPending ? theme.warning
                                             : row.check.collected ? theme.success
                                             : row.check.inLogic ? theme.text : theme.faint
            width: list.width - 10
            height: body.implicitHeight + 12

            Rectangle {
                anchors.fill: parent
                color: row.check.isArmed ? theme.alpha(theme.error, 0.15)
                     : hover.hovered && row.clickable ? theme.alpha(theme.accent, 0.08) : "transparent"
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

                Rectangle {
                    Layout.alignment: Qt.AlignTop
                    Layout.topMargin: 1
                    implicitWidth: tag.implicitWidth + 10
                    implicitHeight: tag.implicitHeight + 2
                    radius: 4
                    color: theme.alpha(row.check.gameTag === "MM" ? theme.mm : theme.gold, 0.16)

                    Text {
                        id: tag
                        anchors.centerIn: parent
                        text: row.check.gameTag
                        color: row.check.gameTag === "MM" ? theme.mmBright : theme.goldBright
                        font.family: theme.font
                        font.pixelSize: 10
                    }
                }

                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: 1

                    Text {
                        Layout.fillWidth: true
                        wrapMode: Text.WordWrap
                        textFormat: Text.PlainText
                        text: row.check.displayName
                        color: row.nameColor
                        font.family: theme.font
                        font.pixelSize: 13
                    }

                    Text {
                        Layout.fillWidth: true
                        visible: row.check.collected && text.length > 0
                        wrapMode: Text.WordWrap
                        textFormat: Text.PlainText
                        text: row.check.itemText
                        color: theme.dim
                        font.family: theme.font
                        font.pixelSize: 12
                    }
                }
            }

            HoverHandler {
                id: hover
                cursorShape: row.clickable ? Qt.PointingHandCursor : Qt.ArrowCursor
            }

            TapHandler {
                enabled: row.clickable || row.check.isArmed
                onTapped: tab.vm.clickVisible(row.index)
            }
        }
    }
}
