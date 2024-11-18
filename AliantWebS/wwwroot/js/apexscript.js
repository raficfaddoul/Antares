function renderChart(value1,value2,value3) {

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

    var chart1 = new ApexCharts(document.querySelector("#chart1"), options);
    chart1.render();
    chart1.updateSeries([value1]);

    var chart2 = new ApexCharts(document.querySelector("#chart2"), options);
    chart2.render();
    chart2.updateSeries([value2]);

    var chart3 = new ApexCharts(document.querySelector("#chart3"), options);
    chart3.render();
    chart3.updateSeries([value3]);
}
