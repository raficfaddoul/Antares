var dygraphInstance1 = null; // Hold the dygraph instance
var dygraphInstance2 = null; // Hold the dygraph instance

function initializeDygraph(divid,xlabel, ylabel) {
    //dygraphInstance = new Dygraph(
    //    document.getElementById("graphdiv"),
    //    "Date,Temperature\n" // Initialize with an empty data set or some default data
    //);

    if (divid == "graphdiv1") {

        headers = `${xlabel},${ylabel}\n`;
        dygraphInstance1 = new Dygraph(document.getElementById(divid), headers, {
            //customBars: true, sharmouta
            //drawPoints: true,
            connectSeparatedPoints: true,
            showRoller: true,
            ylabel: ylabel,
            legend: 'always',
            //  valueRange: [0.0, 100],

            showRangeSelector: true
        });
    }

    else {

        headers = `${xlabel},${ylabel}\n`;
        dygraphInstance2 = new Dygraph(document.getElementById(divid), headers, {
            //customBars: true, sharmouta
            //drawPoints: true,
            connectSeparatedPoints: true,
            showRoller: true,
            ylabel: ylabel,
            legend: 'always',
            //  valueRange: [0.0, 100],

            showRangeSelector: true
        });
    }

}

function updateDygraph(divid,data) {

    try {
        if (divid == "graphdiv1") {

            if (dygraphInstance1) {
                dygraphInstance1.updateOptions({
                    'file': data
                });
            }
        }
        else {

            if (dygraphInstance2) {
                dygraphInstance2.updateOptions({
                    'file': data
                });
            }
        }

    } catch (error) {    
        console.error("Failed to update dygraph:", error);    
    }
}

function updateTimeDygraph(divid,data) {
    
    try {
   
            if (dygraphInstance1) {
                dygraphInstance1.updateOptions({
                    'file': data
                });


                // Get the latest timestamp from the data
                var lastRow = data.split("\n").slice(-2, -1)[0]; // Get the second to last item (last is empty)
                var lastTimestamp = lastRow.split(",")[0];

                // Calculate the 30-second window
                var endDate = new Date(lastTimestamp);
                var startDate = new Date(endDate.getTime() - 10000); // 30 seconds before the last timestamp

                // Update the x-axis range to show only the 30-second window
                dygraphInstance1.updateOptions({
                    dateWindow: [startDate.getTime(), endDate.getTime()]
                });
            }
        
    } catch (error) { 
        console.error("Failed to update dygraph:", error);  
    }
}

function unzoomGraph() {
    g.updateOptions({
        dateWindow: null,
        valueRange: null
    });
}

