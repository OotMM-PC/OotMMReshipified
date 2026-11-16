import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

ColumnLayout {
    id: tab

    property var vm

    spacing: 8

    SectionLabel {
        text: "Appearance"
    }

    Text {
        text: "Accent theme"
        color: theme.text
        font.family: theme.font
        font.pixelSize: 13
    }

    GoldCombo {
        Layout.fillWidth: true
        model: ["Follow the active game", "Always OoT gold", "Always MM violet"]
        currentIndex: tab.vm ? tab.vm.themeIndex : 0
        onActivated: index => tab.vm.themeIndex = index
    }

    Text {
        Layout.topMargin: 4
        text: "UI scale · " + (tab.vm ? tab.vm.uiScalePercent : "")
        color: theme.text
        font.family: theme.font
        font.pixelSize: 13
    }

    GoldSlider {
        Layout.fillWidth: true
        from: 0.75
        to: 1.5
        stepSize: 0.05
        snapMode: Slider.SnapAlways
        value: tab.vm ? tab.vm.uiScale : 1
        onMoved: tab.vm.uiScale = value
        onPressedChanged: if (!pressed) tab.vm.commitUiScale()
    }

    SectionLabel {
        Layout.topMargin: 10
        text: "Controls"
    }

    GoldCheck {
        Layout.fillWidth: true
        text: "Hold to Z-target"
        on: tab.vm ? tab.vm.holdToZTarget : false
        ToolTip.visible: hovered
        ToolTip.delay: 500
        ToolTip.text: "Targeting stays locked only while Z is held. Applies to both games and every seed, straight away."
        onFlipped: value => tab.vm.holdToZTarget = value
    }

    SectionLabel {
        Layout.topMargin: 10
        text: "Quality of life"
    }

    GoldCheck {
        Layout.fillWidth: true
        text: "Roll into pots to break them (OoT)"
        on: tab.vm ? tab.vm.potRolling : true
        ToolTip.visible: hovered
        ToolTip.delay: 500
        ToolTip.text: "Rolling into a pot breaks it in Ocarina of Time, the way it already does in Majora's Mask. Applies straight away."
        onFlipped: value => tab.vm.potRolling = value
    }

    GoldCheck {
        Layout.fillWidth: true
        text: "Show song pads at big fairy spots (OoT)"
        on: tab.vm ? tab.vm.fairySpotPads : false
        ToolTip.visible: hovered
        ToolTip.delay: 500
        ToolTip.text: "Marks the spots where Sun's Song or Song of Storms calls a big fairy, until that spot's check is collected. The original randomizer shows nothing there. Applies straight away."
        onFlipped: value => tab.vm.fairySpotPads = value
    }

    SectionLabel {
        Layout.topMargin: 10
        text: "Multiplayer"
    }

    GoldCheck {
        Layout.fillWidth: true
        text: "Show prompts for items from other players"
        on: tab.vm ? tab.vm.remoteItemPrompts : true
        ToolTip.visible: hovered
        ToolTip.delay: 500
        ToolTip.text: "Shows a background prompt that doesn't stop play. Turn it off to receive these items silently. Junk like rupees and ammo always floats above Link's head either way."
        onFlipped: value => tab.vm.remoteItemPrompts = value
    }

    SectionLabel {
        Layout.topMargin: 10
        text: "Advanced"
    }

    GoldCheck {
        Layout.fillWidth: true
        text: "Enable dev mode (debug and cheat menus)"
        on: tab.vm ? tab.vm.devMode : false
        ToolTip.visible: hovered
        ToolTip.delay: 500
        ToolTip.text: "Both games hide their debug, cheat and enhancement menus by default so seeds stay fair. This unlocks them on the next game boot, along with the force-unlock cheat and manual game switching."
        onFlipped: value => tab.vm.devMode = value
    }

    ColumnLayout {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.cheatGatePending)
        spacing: 6

        Text {
            text: "Pending: restart the game to apply."
            color: theme.warning
            font.family: theme.font
            font.pixelSize: 12
        }

        GoldButton {
            text: "Restart game now"
            onClicked: tab.vm.onRestartClicked()
        }
    }

    Item {
        Layout.fillHeight: true
    }
}
