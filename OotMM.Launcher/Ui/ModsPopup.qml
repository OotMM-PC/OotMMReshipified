import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

PanelPopup {
    id: popup

    property var card
    property int dropIndex: -1

    title: card ? card.title + " mods" : "Mods"
    tint: card && card.isOot ? theme.gold : theme.mm

    onClosed: dropIndex = -1

    Text {
        Layout.fillWidth: true
        wrapMode: Text.WordWrap
        text: "The top mod loads over everything below it. Changes apply the next time the game starts."
        color: theme.dim
        font.family: theme.font
        font.pixelSize: 12
    }

    Text {
        Layout.fillWidth: true
        wrapMode: Text.WordWrap
        text: "Click a name to select it, Ctrl-click or Shift-click to select several, and drag to reorder."
        color: theme.faint
        font.family: theme.font
        font.pixelSize: 12
    }

    RowLayout {
        Layout.fillWidth: true
        spacing: 6

        Repeater {
            model: [
                { label: "⤒ Top", tip: "Move the selected mods to the top (highest priority)." },
                { label: "▲ Up", tip: "Move the selected mods up one slot." },
                { label: "▼ Down", tip: "Move the selected mods down one slot." },
                { label: "⤓ Bottom", tip: "Move the selected mods to the bottom (lowest priority)." }
            ]

            delegate: Chip {
                required property var modelData
                required property int index
                text: modelData.label
                tint: popup.tint
                enabled: !!(popup.card && popup.card.hasSelection)
                ToolTip.visible: hovered
                ToolTip.delay: 500
                ToolTip.text: modelData.tip
                onClicked: {
                    if (index === 0) popup.card.moveSelectedTop()
                    else if (index === 1) popup.card.moveSelectedUp()
                    else if (index === 2) popup.card.moveSelectedDown()
                    else popup.card.moveSelectedBottom()
                }
            }
        }

        Item {
            Layout.fillWidth: true
        }

        LinkButton {
            visible: !!(popup.card && popup.card.hasSelection)
            text: "Clear selection"
            tint: popup.tint
            font.pixelSize: 12
            onClicked: popup.card.clearSelection()
        }
    }

    ListView {
        id: list
        Layout.fillWidth: true
        Layout.fillHeight: true
        Layout.preferredHeight: contentHeight
        Layout.minimumHeight: Math.min(contentHeight, 140)
        clip: true
        spacing: 2
        boundsBehavior: Flickable.StopAtBounds
        model: popup.card ? popup.card.mods : []
        ScrollBar.vertical: ScrollBar {
            policy: list.contentHeight > list.height ? ScrollBar.AlwaysOn : ScrollBar.AlwaysOff
        }

        function insertionAt(y) {
            var contentY = y + list.contentY
            for (var i = 0; i < list.count; i++) {
                var item = list.itemAtIndex(i)
                if (item && contentY < item.y + item.height / 2)
                    return i
            }
            return list.count
        }

        function markerY(index) {
            if (list.count === 0)
                return 0
            var item = list.itemAtIndex(Math.min(index, list.count - 1))
            if (!item)
                return -10
            return index >= list.count ? item.y + item.height + 1 : item.y - 1
        }

        delegate: Rectangle {
            id: row

            required property int index
            readonly property var mod: popup.card.modAt(index)

            width: list.width - (list.contentHeight > list.height ? 12 : 0)
            height: 32
            radius: 5
            color: row.mod.isSelected ? theme.alpha(popup.tint, 0.18)
                 : rowArea.containsMouse ? theme.alpha(popup.tint, 0.06) : theme.panel
            border.width: 1
            border.color: row.mod.isSelected ? popup.tint : "transparent"

            MouseArea {
                id: rowArea

                property real pressY: 0
                property bool dragging: false
                property bool collapse: false

                anchors.fill: parent
                hoverEnabled: true
                preventStealing: true
                acceptedButtons: Qt.LeftButton

                onPressed: mouse => {
                    pressY = mouse.y
                    dragging = false
                    collapse = false
                    var ctrl = (mouse.modifiers & Qt.ControlModifier) !== 0
                    var shift = (mouse.modifiers & Qt.ShiftModifier) !== 0
                    if (ctrl || shift)
                        popup.card.selectMod(row.index, ctrl, shift)
                    else if (!row.mod.isSelected)
                        popup.card.selectMod(row.index, false, false)
                    else
                        collapse = true
                }

                onPositionChanged: mouse => {
                    if (!pressed)
                        return
                    if (!dragging && Math.abs(mouse.y - pressY) > 6)
                        dragging = true
                    if (!dragging)
                        return
                    var p = mapToItem(list, mouse.x, mouse.y)
                    if (p.y < 16 && list.contentY > 0)
                        list.contentY = Math.max(0, list.contentY - 12)
                    else if (p.y > list.height - 16 && list.contentY < list.contentHeight - list.height)
                        list.contentY = Math.min(list.contentHeight - list.height, list.contentY + 12)
                    popup.dropIndex = list.insertionAt(p.y)
                }

                onReleased: {
                    if (dragging && popup.dropIndex >= 0)
                        popup.card.moveSelectedTo(popup.dropIndex)
                    else if (collapse)
                        popup.card.selectMod(row.index, false, false)
                    dragging = false
                    collapse = false
                    popup.dropIndex = -1
                }

                onCanceled: {
                    dragging = false
                    collapse = false
                    popup.dropIndex = -1
                }
            }

            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 6
                anchors.rightMargin: 8
                spacing: 6

                LinkButton {
                    text: "▲"
                    tint: popup.tint
                    enabled: !row.mod.isFirst
                    ToolTip.visible: hovered
                    ToolTip.delay: 500
                    ToolTip.text: "Raise priority (load over the mod above)."
                    onClicked: popup.card.moveModAt(row.index, -1)
                }

                LinkButton {
                    text: "▼"
                    tint: popup.tint
                    enabled: !row.mod.isLast
                    ToolTip.visible: hovered
                    ToolTip.delay: 500
                    ToolTip.text: "Lower priority (let the mod above load over this one)."
                    onClicked: popup.card.moveModAt(row.index, 1)
                }

                GoldCheck {
                    tint: popup.tint
                    on: row.mod.enabled
                    enabled: !row.mod.required
                    ToolTip.visible: hovered
                    ToolTip.delay: 500
                    ToolTip.text: row.mod.required ? "OoTMM needs this archive, so it is always on."
                                                         : "Enable or disable this archive."
                    onFlipped: value => popup.card.setModEnabled(row.index, value)
                }

                Text {
                    Layout.fillWidth: true
                    text: row.mod.name
                    color: row.mod.required ? theme.dim : row.mod.enabled ? theme.text : theme.faint
                    elide: Text.ElideMiddle
                    font.family: theme.font
                    font.pixelSize: 13
                }

                Text {
                    visible: row.mod.required
                    text: "required"
                    color: theme.faint
                    font.family: theme.font
                    font.pixelSize: 11
                }
            }
        }

        Rectangle {
            visible: popup.dropIndex >= 0
            x: 0
            y: list.markerY(popup.dropIndex) - 1
            width: list.width - (list.contentHeight > list.height ? 12 : 0)
            height: 2
            radius: 1
            color: popup.tint
            z: 10
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
