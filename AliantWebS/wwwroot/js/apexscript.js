// Store chart instances to avoid duplicates
var _chart1Instance = null;
var _chart2Instance = null;
var _chart3Instance = null;

function renderChart(value1, value2, value3) {

    var options = {
        series: [0],
        chart: {
            type: 'radialBar',
            offsetY: -20,
            sparkline: {
                enabled: true
            }
        },
        plotOptions: {
            radialBar: {
                startAngle: -90,
                endAngle: 90,
                track: {
                    background: "#e7e7e7",
                    strokeWidth: '97%',
                    margin: 5, // margin is in pixels
                    dropShadow: {
                        enabled: true,
                        top: 2,
                        left: 0,
                        color: '#999',
                        opacity: 1,
                        blur: 2
                    }
                },
                dataLabels: {
                    name: {
                        show: false
                    },
                    value: {
                        offsetY: -2,
                        fontSize: '22px'
                    }
                }
            }
        },
        grid: {
            padding: {
                top: -10
            }
        },
        fill: {
            type: 'gradient',
            gradient: {
                shade: 'light',
                shadeIntensity: 0.4,
                inverseColors: false,
                opacityFrom: 1,
                opacityTo: 1,
                stops: [0, 50, 53, 91]
            },
        },
        labels: ['Average Results'],
    };

    // Helper: create or update a single gauge, handling stale instances after navigation
    function renderGauge(instance, selector, value) {
        var el = document.querySelector(selector);
        if (!el) return null;

        // If instance exists but its container was removed from DOM (navigation), destroy it
        if (instance) {
            try {
                if (!document.body.contains(instance.el)) {
                    instance.destroy();
                    instance = null;
                }
            } catch (e) {
                instance = null;
            }
        }

        if (instance) {
            instance.updateSeries([value]);
        } else {
            el.innerHTML = ''; // Clear any leftover content
            instance = new ApexCharts(el, options);
            instance.render();
            instance.updateSeries([value]);
        }
        return instance;
    }

    _chart1Instance = renderGauge(_chart1Instance, "#chart1", value1);
    _chart2Instance = renderGauge(_chart2Instance, "#chart2", value2);
    _chart3Instance = renderGauge(_chart3Instance, "#chart3", value3);
}
