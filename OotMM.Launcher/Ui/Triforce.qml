import QtQuick
import QtQuick.Shapes

Item {
    id: root

    property color color: theme.accent

    implicitWidth: 40
    implicitHeight: 36

    Shape {
        width: 20
        height: 18
        preferredRendererType: Shape.CurveRenderer
        transform: Scale {
            xScale: root.width / 20
            yScale: root.height / 18
        }

        ShapePath {
            fillColor: root.color
            strokeWidth: -1
            PathSvg {
                path: "M10 0 L15 9 L5 9 Z M5 9 L10 18 L0 18 Z M15 9 L20 18 L10 18 Z"
            }
        }
    }
}
