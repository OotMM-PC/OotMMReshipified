import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

PanelPopup {
    id: popup

    property var config
    property bool isOot: true
    property var card: config ? (isOot ? config.ootCard : config.mmCard) : null
    property var picks: config ? (isOot ? config.ootModelPicks : config.mmModelPicks) : []

    title: isOot ? "Ocarina of Time player model" : "Majora's Mask player model"
    tint: isOot ? theme.gold : theme.mm

    Text {
        Layout.fillWidth: true
        wrapMode: Text.WordWrap
        text: "Check every archive your model needs: child, adult, textures and voice. In co-op the set is shared with the other players. With none checked, Link stays vanilla."
        color: theme.dim
        font.family: theme.font
        font.pixelSize: 12
    }

    ListView {
        id: list
        Layout.fillWidth: true
        Layout.fillHeight: true
        Layout.preferredHeight: contentHeight
        Layout.minimumHeight: Math.min(contentHeight, 120)
        visible: count > 0
        clip: true
        boundsBehavior: Flickable.StopAtBounds
        model: popup.picks
        ScrollBar.vertical: ScrollBar {
            policy: list.contentHeight > list.height ? ScrollBar.AlwaysOn : ScrollBar.AlwaysOff
        }

        delegate: GoldCheck {
            required property int index
            readonly property var pick: popup.config.modelPickAt(popup.isOot, index)
            width: list.width - (list.contentHeight > list.height ? 12 : 0)
            height: 28
            wrap: false
            tint: popup.tint
            text: pick.name
            on: pick.isSelected
            onFlipped: value => popup.config.setModelPick(popup.isOot, index, value)
        }
    }

    Text {
        visible: list.count === 0
        text: "No model archives in this game's mods folder."
        color: theme.faint
        font.family: theme.font
        font.pixelSize: 12
    }

    ColumnLayout {
        Layout.fillWidth: true
        visible: !popup.isOot
        spacing: 6

        Rectangle {
            Layout.fillWidth: true
            height: 1
            color: theme.alpha(popup.tint, 0.25)
        }

        GoldCheck {
            Layout.fillWidth: true
            tint: popup.tint
            text: "Use my OoT model for adult Link"
            on: popup.card ? popup.card.portAdultModel : false
            ToolTip.visible: hovered
            ToolTip.delay: 500
            ToolTip.text: "Adult Link in MM renders your selected OoT model. Unchecked, the vanilla adult is used."
            onFlipped: value => popup.card.portAdultModel = value
        }

        GoldCheck {
            Layout.fillWidth: true
            tint: popup.tint
            text: "Use my OoT child model as MM's human Link"
            on: popup.card ? popup.card.portChildModel : false
            onFlipped: value => popup.card.portChildModel = value
        }

        Text {
            Layout.fillWidth: true
            wrapMode: Text.WordWrap
            text: "The child model overrides MM human-form model mods. Deku, Goron, Zora and Fierce Deity keep their MM mods."
            color: theme.dim
            font.family: theme.font
            font.pixelSize: 12
        }
    }
}
