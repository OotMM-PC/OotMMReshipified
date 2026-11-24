import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

Item {
    id: tab

    property var vm
    property bool wide: false
    property int hot: -1
    readonly property bool usable: !!(vm && vm.available)
    readonly property real heightLimit: wide ? Math.max(120, card.height - 2)
                                             : Math.min(290, Math.max(180, tab.height * 0.46))
    readonly property real mapWidth: !usable ? 0
        : Math.floor(Math.min(Math.max(0, card.width - 2), heightLimit * vm.imageWidth / vm.imageHeight))
    readonly property real mapHeight: !usable ? 0 : Math.floor(mapWidth * vm.imageHeight / vm.imageWidth)

    onMapWidthChanged: relayout()
    onMapHeightChanged: relayout()

    function closeAreas() {
        areaMenu.close()
    }

    function relayout() {
        if (usable && mapWidth > 0 && mapHeight > 0) {
            vm.layoutPins(mapWidth, mapHeight)
        }
    }

    component Marker: Item {
        id: marker

        property string look: "found"
        property string label: ""
        property bool dungeon: false
        property bool grotto: false
        property bool came: false
        property bool lit: false
        property bool pulse: false
        readonly property bool small: look === "fixed" || look === "hole"
        readonly property real size: small ? 10 : 20

        width: size
        height: size
        scale: lit ? 1.35 : 1

        Rectangle {
            anchors.centerIn: parent
            visible: marker.came
            width: marker.size + 10
            height: width
            radius: width / 2
            color: "transparent"
            border.width: 3
            border.color: theme.success
        }

        Rectangle {
            id: halo
            anchors.centerIn: parent
            visible: marker.pulse && marker.look === "open"
            width: marker.size + 12
            height: width
            radius: width / 2
            color: "#FFFFFF"
            opacity: 0

            SequentialAnimation on opacity {
                running: halo.visible
                loops: Animation.Infinite
                NumberAnimation { to: 0.28; duration: 900; easing.type: Easing.InOutQuad }
                NumberAnimation { to: 0; duration: 900; easing.type: Easing.InOutQuad }
            }
        }

        Rectangle {
            anchors.centerIn: parent
            width: marker.size + 4
            height: width
            radius: marker.dungeon ? 5 : marker.grotto || marker.look === "fixed" ? 6 : width / 2
            rotation: marker.dungeon ? 45 : 0
            color: marker.lit ? "#FFFFFF" : "#D9000000"
        }

        Rectangle {
            anchors.centerIn: parent
            width: marker.size
            height: marker.size
            rotation: marker.dungeon ? 45 : 0
            radius: marker.dungeon ? 4 : marker.look === "fixed" ? 3 : marker.grotto ? 5 : width / 2
            color: marker.look === "found" ? theme.accent
                 : marker.look === "exit" ? theme.success
                 : marker.look === "fixed" ? "#7D8F93"
                 : marker.look === "hole" ? "#15120C" : theme.bgDeep
            border.width: marker.look === "open" || marker.look === "hole" ? 2 : 0
            border.color: marker.look === "hole" ? "#9AA7AA" : "#FFFFFF"
        }

        Text {
            anchors.centerIn: parent
            visible: !marker.small
            text: marker.label
            color: marker.look === "open" ? "#FFFFFF" : marker.look === "exit" ? "#0B1A08" : theme.accentInk
            font.family: theme.font
            font.pixelSize: marker.look === "exit" ? 13 : 11
            font.bold: true
        }
    }

    component LegendEntry: Row {
        property string look: "found"
        property bool dungeon: false
        property bool grotto: false
        property string label: ""

        spacing: 5

        Marker {
            anchors.verticalCenter: parent.verticalCenter
            scale: 0.5
            transformOrigin: Item.Center
            look: parent.look
            dungeon: parent.dungeon
            grotto: parent.grotto
            width: 10
            height: 10
        }

        Text {
            anchors.verticalCenter: parent.verticalCenter
            text: parent.label
            color: theme.faint
            font.family: theme.font
            font.pixelSize: 11
        }
    }

    Popup {
        id: areaMenu
        parent: picker
        y: picker.height + 4
        width: Math.min(picker.width + 90, 350)
        height: Math.min(areaList.contentHeight + 8, 420)
        padding: 4

        background: Rectangle {
            radius: 8
            color: theme.raised
            border.width: 1
            border.color: theme.accent
        }

        contentItem: ListView {
            id: areaList
            clip: true
            model: tab.vm ? tab.vm.areas : []
            ScrollBar.vertical: ScrollBar { }

            delegate: Item {
                id: entry
                required property var modelData
                required property int index
                width: areaList.width - 8
                height: entry.modelData.isHeader ? 24 : 28

                Text {
                    visible: entry.modelData.isHeader
                    x: 8
                    anchors.bottom: parent.bottom
                    anchors.bottomMargin: 3
                    text: entry.modelData.title.toUpperCase()
                    color: theme.faint
                    font.family: theme.font
                    font.pixelSize: 10
                    font.letterSpacing: 0.8
                }

                Rectangle {
                    anchors.fill: parent
                    visible: !entry.modelData.isHeader
                    radius: 5
                    color: entry.modelData.selected || entryHover.hovered ? theme.field : "transparent"

                    RowLayout {
                        anchors.fill: parent
                        anchors.leftMargin: 8
                        anchors.rightMargin: 8
                        spacing: 8

                        Text {
                            Layout.fillWidth: true
                            text: entry.modelData.title
                            elide: Text.ElideRight
                            color: theme.text
                            font.family: theme.font
                            font.pixelSize: 13
                        }

                        Text {
                            visible: entry.modelData.isHere
                            text: "●"
                            color: theme.success
                            font.pixelSize: 11
                        }

                        Text {
                            text: entry.modelData.countText
                            color: entry.modelData.complete ? theme.success : theme.dim
                            font.family: theme.font
                            font.pixelSize: 12
                        }
                    }

                    HoverHandler {
                        id: entryHover
                        cursorShape: Qt.PointingHandCursor
                    }

                    TapHandler {
                        onTapped: {
                            tab.vm.selectArea(entry.index)
                            tab.closeAreas()
                        }
                    }
                }
            }
        }
    }

    Text {
        anchors.fill: parent
        visible: !tab.usable
        wrapMode: Text.WordWrap
        text: "Map pictures are not installed with this launcher, so there is nothing to show here."
        color: theme.faint
        font.family: theme.font
        font.pixelSize: 12
    }

    ColumnLayout {
        anchors.fill: parent
        visible: tab.usable
        spacing: 8

        RowLayout {
            Layout.fillWidth: true
            spacing: 8

            Button {
                id: picker
                Layout.fillWidth: true
                implicitHeight: 32
                hoverEnabled: true
                onClicked: areaMenu.opened ? areaMenu.close() : areaMenu.open()

                contentItem: RowLayout {
                    spacing: 8

                    Rectangle {
                        Layout.leftMargin: 9
                        implicitWidth: pickTag.implicitWidth + 10
                        implicitHeight: pickTag.implicitHeight + 2
                        radius: 4
                        color: theme.alpha(tab.vm && tab.vm.gameTag === "MM" ? theme.mm : theme.gold, 0.18)

                        Text {
                            id: pickTag
                            anchors.centerIn: parent
                            text: tab.vm ? tab.vm.gameTag : ""
                            color: tab.vm && tab.vm.gameTag === "MM" ? theme.mmBright : theme.goldBright
                            font.family: theme.font
                            font.pixelSize: 10
                            font.weight: Font.DemiBold
                        }
                    }

                    Text {
                        Layout.fillWidth: true
                        text: tab.vm ? tab.vm.title : ""
                        elide: Text.ElideRight
                        color: theme.text
                        font.family: theme.font
                        font.pixelSize: 14
                        font.weight: Font.DemiBold
                    }

                    Text {
                        visible: !!(tab.vm && tab.vm.linkIsHere && !tab.vm.showingInterior)
                        text: "● here"
                        color: theme.success
                        font.family: theme.font
                        font.pixelSize: 11
                    }

                    Text {
                        Layout.rightMargin: 9
                        text: "▾"
                        color: theme.accent
                        font.pixelSize: 12
                    }
                }

                background: Rectangle {
                    radius: 6
                    color: theme.field
                    border.width: 1
                    border.color: picker.hovered || areaMenu.opened ? theme.accent : theme.alpha(theme.accent, 0.45)
                }
            }

            GoldCheck {
                text: "Follow Link"
                wrap: false
                on: tab.vm ? tab.vm.follow : true
                ToolTip.visible: hovered
                ToolTip.delay: 500
                ToolTip.text: "Keep the map on the area Link is in."
                onFlipped: value => tab.vm.follow = value
            }

            Button {
                implicitWidth: 26
                implicitHeight: 26
                hoverEnabled: true
                onClicked: tab.vm.expanded = !tab.vm.expanded
                ToolTip.visible: hovered
                ToolTip.delay: 500
                ToolTip.text: tab.wide ? "Back to the narrow sidebar" : "Expand the map"

                contentItem: Text {
                    text: tab.wide ? "✕" : "⤢"
                    color: theme.accent
                    font.pixelSize: 14
                    horizontalAlignment: Text.AlignHCenter
                    verticalAlignment: Text.AlignVCenter
                }

                background: Rectangle {
                    radius: 6
                    color: parent.hovered ? theme.raised : theme.field
                    border.width: 1
                    border.color: theme.alpha(theme.accent, 0.45)
                }
            }
        }

        Field {
            id: search
            Layout.fillWidth: true
            rightPadding: clearSearch.visible ? clearSearch.width + 12 : 9
            placeholderText: "Search entrances"
            text: tab.vm ? tab.vm.searchText : ""
            onTextEdited: tab.vm.searchText = text
            onAccepted: focus = false
            Keys.onEscapePressed: {
                tab.vm.searchText = ""
                focus = false
            }

            LinkButton {
                id: clearSearch
                anchors.right: parent.right
                anchors.rightMargin: 9
                anchors.verticalCenter: parent.verticalCenter
                visible: search.text.length > 0
                text: "✕"
                ToolTip.visible: hovered
                ToolTip.delay: 500
                ToolTip.text: "Clear the search"
                onClicked: {
                    tab.vm.searchText = ""
                    search.forceActiveFocus()
                }
            }
        }

        Rectangle {
            Layout.fillWidth: true
            visible: !!(tab.vm && tab.vm.notice.length > 0)
            implicitHeight: noticeText.implicitHeight + 10
            radius: 6
            color: theme.bgDeep
            border.width: 1
            border.color: tab.vm && tab.vm.noticeJumps ? theme.alpha(theme.accent, 0.45) : theme.success

            Text {
                id: noticeText
                anchors.fill: parent
                anchors.margins: 5
                anchors.leftMargin: 8
                text: tab.vm ? tab.vm.notice : ""
                elide: Text.ElideRight
                color: tab.vm && tab.vm.noticeJumps ? theme.accentBright : theme.success
                font.family: theme.font
                font.pixelSize: 12
            }

            HoverHandler {
                cursorShape: tab.vm && tab.vm.noticeJumps ? Qt.PointingHandCursor : Qt.ArrowCursor
            }

            TapHandler {
                enabled: !!(tab.vm && tab.vm.noticeJumps)
                onTapped: tab.vm.showLink()
            }
        }

        GridLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            columns: tab.wide ? 2 : 1
            columnSpacing: 12
            rowSpacing: 8

            Rectangle {
                id: card
                Layout.fillWidth: true
                Layout.fillHeight: tab.wide
                Layout.preferredHeight: tab.wide ? -1 : tab.mapHeight + 2
                visible: !!(tab.vm && tab.vm.hasPicture)
                radius: 8
                color: theme.bgDeep
                border.width: 1
                border.color: theme.alpha(theme.accent, 0.45)
                clip: true

                Item {
                    id: map
                    anchors.centerIn: parent
                    width: tab.mapWidth
                    height: tab.mapHeight

                    Image {
                        anchors.fill: parent
                        source: tab.vm ? tab.vm.imageSource : ""
                        asynchronous: true
                        smooth: true
                        mipmap: true
                    }

                    Rectangle {
                        anchors.fill: parent
                        visible: !!(tab.vm && !tab.vm.showingInterior)
                        color: "#000000"
                        opacity: 0.2
                    }

                    Repeater {
                        model: tab.vm ? tab.vm.pins : []

                        delegate: Item {
                            id: leader
                            required property var modelData
                            readonly property real dx: modelData.x - modelData.anchorX
                            readonly property real dy: modelData.y - modelData.anchorY
                            visible: modelData.displaced
                            x: modelData.anchorX
                            y: modelData.anchorY

                            Rectangle {
                                width: Math.sqrt(leader.dx * leader.dx + leader.dy * leader.dy)
                                height: 1
                                color: "#C0FFFFFF"
                                transformOrigin: Item.Left
                                rotation: Math.atan2(leader.dy, leader.dx) * 180 / Math.PI
                            }

                            Rectangle {
                                x: -2
                                y: -2
                                width: 4
                                height: 4
                                radius: 2
                                color: "#FFFFFF"
                            }
                        }
                    }

                    Repeater {
                        model: tab.vm ? tab.vm.pins : []

                        delegate: Marker {
                            id: pin
                            required property var modelData
                            x: modelData.x - width / 2
                            y: modelData.y - height / 2
                            z: lit ? 5 : 0
                            look: modelData.style
                            label: modelData.number
                            dungeon: modelData.dungeon
                            grotto: modelData.grotto
                            came: modelData.came
                            pulse: true
                            lit: tab.hot === modelData.pinIndex
                            opacity: tab.hot !== -1 && !lit ? 0.35 : 1

                            HoverHandler {
                                cursorShape: Qt.PointingHandCursor
                                onHoveredChanged: tab.hot = hovered ? pin.modelData.pinIndex
                                                : tab.hot === pin.modelData.pinIndex ? -1 : tab.hot
                            }

                            TapHandler {
                                onTapped: tab.vm.activatePin(pin.modelData.pinIndex)
                            }

                            ToolTip.visible: lit
                            ToolTip.delay: 400
                            ToolTip.text: modelData.tip
                        }
                    }
                }
            }

            ColumnLayout {
                Layout.fillWidth: !tab.wide || !card.visible
                Layout.preferredWidth: tab.wide && card.visible ? 272 : -1
                Layout.fillHeight: true
                spacing: 5

                RowLayout {
                    Layout.fillWidth: true
                    spacing: 10

                    Text {
                        Layout.fillWidth: true
                        text: tab.vm ? tab.vm.countText : ""
                        elide: Text.ElideRight
                        color: tab.vm && tab.vm.countComplete ? theme.success : theme.dim
                        font.family: theme.font
                        font.pixelSize: 12
                    }

                    GoldCheck {
                        visible: !!(tab.vm && ((tab.vm.hasPicture && !tab.vm.showingInterior) || tab.vm.searching))
                        text: "Fixed entrances"
                        wrap: false
                        on: tab.vm ? tab.vm.showFixed : false
                        ToolTip.visible: hovered
                        ToolTip.delay: 500
                        ToolTip.text: "Also show entrances this seed does not shuffle."
                        onFlipped: value => tab.vm.showFixed = value
                    }
                }

                Flow {
                    Layout.fillWidth: true
                    visible: !!(tab.vm && tab.vm.hasPicture && !tab.vm.showingInterior)
                    spacing: 12

                    LegendEntry { label: "area" }
                    LegendEntry { label: "dungeon"; dungeon: true }
                    LegendEntry { label: "grotto"; grotto: true }
                    LegendEntry { label: "not explored"; look: "open" }
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
                    model: tab.vm ? tab.vm.rows : []
                    ScrollBar.vertical: ScrollBar { }

                    delegate: Item {
                        id: row
                        required property var modelData
                        required property int index
                        readonly property bool header: modelData.kind === 0
                        readonly property bool clickable: modelData.kind === 3 || modelData.target.length > 0
                        readonly property bool lit: modelData.pinIndex !== -1 && tab.hot === modelData.pinIndex
                        width: list.width - 10
                        height: header ? 26 : modelData.kind === 3 ? 30 : 44

                        Text {
                            visible: row.header
                            x: 2
                            anchors.bottom: parent.bottom
                            anchors.bottomMargin: 4
                            text: row.modelData.text.toUpperCase()
                            color: theme.faint
                            font.family: theme.font
                            font.pixelSize: 10
                            font.letterSpacing: 0.8
                        }

                        Rectangle {
                            anchors.fill: parent
                            visible: !row.header
                            radius: 6
                            color: row.lit || (rowHover.hovered && row.clickable) ? theme.alpha(theme.accent, 0.12)
                                                                                   : "transparent"
                        }

                        Rectangle {
                            anchors.bottom: parent.bottom
                            visible: !row.header
                            width: parent.width
                            height: 1
                            color: theme.alpha(theme.accent, 0.1)
                        }

                        RowLayout {
                            anchors.fill: parent
                            anchors.leftMargin: 6
                            anchors.rightMargin: 6
                            visible: !row.header
                            spacing: 10

                            Item {
                                visible: row.modelData.style.length > 0
                                implicitWidth: 20
                                implicitHeight: 20

                                Marker {
                                    anchors.centerIn: parent
                                    look: row.modelData.style
                                    label: row.modelData.number
                                    dungeon: row.modelData.dungeon
                                    grotto: row.modelData.grotto
                                }
                            }

                            ColumnLayout {
                                Layout.fillWidth: true
                                spacing: 0

                                Text {
                                    Layout.fillWidth: true
                                    textFormat: Text.PlainText
                                    text: row.modelData.text
                                    elide: Text.ElideRight
                                    color: row.modelData.kind === 3 ? theme.faint : theme.text
                                    font.family: theme.font
                                    font.pixelSize: row.modelData.kind === 3 ? 12 : 13
                                }

                                RowLayout {
                                    Layout.fillWidth: true
                                    visible: row.modelData.detail.length > 0
                                    spacing: 6

                                    Text {
                                        Layout.fillWidth: true
                                        textFormat: Text.PlainText
                                        text: row.modelData.detail
                                        elide: Text.ElideRight
                                        color: row.modelData.style === "open" ? theme.faint : theme.accentBright
                                        font.family: theme.font
                                        font.pixelSize: 12
                                    }

                                    Rectangle {
                                        visible: row.modelData.tag.length > 0
                                        implicitWidth: destTag.implicitWidth + 10
                                        implicitHeight: destTag.implicitHeight + 2
                                        radius: 4
                                        color: theme.alpha(row.modelData.tag === "MM" ? theme.mm : theme.gold, 0.18)

                                        Text {
                                            id: destTag
                                            anchors.centerIn: parent
                                            text: row.modelData.tag
                                            color: row.modelData.tag === "MM" ? theme.mmBright : theme.goldBright
                                            font.family: theme.font
                                            font.pixelSize: 10
                                        }
                                    }
                                }
                            }

                            Rectangle {
                                visible: row.modelData.pill.length > 0
                                implicitWidth: pillText.implicitWidth + 14
                                implicitHeight: pillText.implicitHeight + 2
                                radius: height / 2
                                color: "transparent"
                                border.width: 1
                                border.color: theme.success

                                Text {
                                    id: pillText
                                    anchors.centerIn: parent
                                    text: row.modelData.pill
                                    color: theme.success
                                    font.family: theme.font
                                    font.pixelSize: 10
                                }
                            }
                        }

                        HoverHandler {
                            id: rowHover
                            cursorShape: row.clickable ? Qt.PointingHandCursor : Qt.ArrowCursor
                            onHoveredChanged: {
                                if (row.modelData.pinIndex === -1) {
                                    return
                                }
                                tab.hot = hovered ? row.modelData.pinIndex
                                        : tab.hot === row.modelData.pinIndex ? -1 : tab.hot
                            }
                        }

                        TapHandler {
                            enabled: row.clickable
                            onTapped: tab.vm.activateRow(row.index)
                        }
                    }
                }
            }
        }
    }
}
