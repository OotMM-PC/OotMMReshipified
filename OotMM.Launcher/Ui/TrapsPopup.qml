import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

PanelPopup {
    id: popup

    property var traps

    title: "Traps"

    component TrapSlider: ColumnLayout {
        id: row

        property string label
        property string valueText
        property string tip
        property real value
        property real from
        property real to
        property real step

        signal moved(real value)

        Layout.fillWidth: true
        spacing: 2

        Text {
            Layout.fillWidth: true
            text: row.label + " · " + row.valueText
            color: theme.text
            font.family: theme.font
            font.pixelSize: 13
            ToolTip.visible: tipArea.hovered
            ToolTip.delay: 500
            ToolTip.text: row.tip

            HoverHandler {
                id: tipArea
            }
        }

        GoldSlider {
            Layout.fillWidth: true
            from: row.from
            to: row.to
            stepSize: row.step
            snapMode: Slider.SnapAlways
            value: row.value
            onMoved: row.moved(value)
            onPressedChanged: if (!pressed && popup.traps) popup.traps.commit()
        }
    }

    Text {
        Layout.fillWidth: true
        wrapMode: Text.WordWrap
        text: "Server rule: these apply to every player in the session. The defaults are the original traps."
        color: theme.dim
        font.family: theme.font
        font.pixelSize: 12
    }

    ScrollView {
        Layout.fillWidth: true
        Layout.preferredHeight: Math.min(column.implicitHeight, 420)
        clip: true
        contentWidth: availableWidth

        ColumnLayout {
            id: column

            width: parent.width
            spacing: 6

            TrapSlider {
                label: "Ice break-out effort"
                valueText: popup.traps ? popup.traps.iceBreakoutText : ""
                tip: "How much mashing it takes to break out of an ice trap. 100% is the original."
                from: 25
                to: 400
                step: 25
                value: popup.traps ? popup.traps.iceBreakoutPercent : 100
                onMoved: value => popup.traps.iceBreakoutPercent = value
            }

            TrapSlider {
                label: "Ice trap damage over time"
                valueText: popup.traps ? popup.traps.iceDamageText : ""
                tip: "Health lost while frozen, as a percent of the original drain. 100% is the original."
                from: 0
                to: 1000
                step: 25
                value: popup.traps ? popup.traps.iceDamagePercent : 100
                onMoved: value => popup.traps.iceDamagePercent = value
            }

            TrapSlider {
                label: "Fire trap damage over time"
                valueText: popup.traps ? popup.traps.fireDamageText : ""
                tip: "Health lost while burning, as a percent of the original burn. 100% is the original."
                from: 0
                to: 1000
                step: 25
                value: popup.traps ? popup.traps.fireDamagePercent : 100
                onMoved: value => popup.traps.fireDamagePercent = value
            }

            TrapSlider {
                label: "Fire trap burn"
                valueText: popup.traps ? popup.traps.fireSecondsText : ""
                tip: "Longest time Link stays on fire. The original is up to 10 seconds."
                from: 0
                to: 30
                step: 0.5
                value: popup.traps ? popup.traps.fireSeconds : 10
                onMoved: value => popup.traps.fireSeconds = value
            }

            TrapSlider {
                label: "Shock trap damage over time"
                valueText: popup.traps ? popup.traps.shockDamageText : ""
                tip: "Health lost while shocked, as a percent of the original rate. The original 1 second shock ends before it deals any."
                from: 0
                to: 1000
                step: 25
                value: popup.traps ? popup.traps.shockDamagePercent : 100
                onMoved: value => popup.traps.shockDamagePercent = value
            }

            TrapSlider {
                label: "Shock trap length"
                valueText: popup.traps ? popup.traps.shockSecondsText : ""
                tip: "How long the shock holds Link. The original is 1 second."
                from: 0.5
                to: 10
                step: 0.5
                value: popup.traps ? popup.traps.shockSeconds : 1
                onMoved: value => popup.traps.shockSeconds = value
            }

            Text {
                Layout.topMargin: 4
                text: "Knockback distances"
                color: theme.text
                font.family: theme.font
                font.pixelSize: 13
            }

            Field {
                Layout.fillWidth: true
                placeholderText: "100:0-600"
                text: popup.traps ? popup.traps.knockbackText : ""
                ToolTip.visible: hovered
                ToolTip.delay: 500
                ToolTip.text: "Chance and distance pairs, for example 25:200, 50:60-120. That is a 25% chance of 200 units and a 50% chance of anywhere from 60 to 120. Link is about 50 units tall. The original is 100:0-600. Whatever chance is left over uses the original random knockback."
                onEditingFinished: popup.traps.knockbackText = text
            }

            Text {
                Layout.fillWidth: true
                visible: !!(popup.traps && popup.traps.knockbackError.length > 0)
                text: popup.traps ? popup.traps.knockbackError : ""
                wrapMode: Text.WordWrap
                color: theme.warning
                font.family: theme.font
                font.pixelSize: 12
            }
        }
    }

    GoldButton {
        text: "Reset to original"
        onClicked: popup.traps.reset()
    }
}
