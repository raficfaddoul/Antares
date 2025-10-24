var dygraphInstance1 = null; // Hold the dygraph instance
var dygraphInstance2 = null; // Hold the dygraph instance

var dygraphInstances = {};

window.dygraphInterop = {
    // Dictionary of Dygraph instances so we can handle multiple graphs
    dygraphInstances: {},

    // Initialize a Dygraph using a 2D array
    // data is expected to be an array of arrays: [[x1, y1, y2, ...], [x2, y1, y2, ...], ...]
    initializeDygraphArray: function (divId, data, labels) {
        const options = {
            labels: labels,         // e.g. ["Date", "Series A", "Series B", ...]
            showRoller: true,
            connectSeparatedPoints: true,
            legend: 'always',
            labelsSeparateLines: true,
            showRangeSelector: true,
            highlightCircleSize: 4,
            highlightSeriesOpts: {
                strokeWidth: 2,
                strokeBorderWidth: 1,
                highlightCircleSize: 5
            }
            // ... any other Dygraph options ...
        };

        // Create a new Dygraph instance and store it
        const elem = document.getElementById(divId);
        const g = new Dygraph(elem, data, options);
        this.dygraphInstances[divId] = g;
    },

    // Update the Dygraph data using a new 2D array
    updateDygraphArray: function (divId, data, labels) {
        const g = this.dygraphInstances[divId];
        if (g) {
            g.updateOptions({
                file: data,
                labels: labels
            });
        }
    }
};


function initializeDygraph(divid,xlabel, ylabel) {
    //dygraphInstance = new Dygraph(
    //    document.getElementById("graphdiv"),
    //    "Date,Temperature\n" // Initialize with an empty data set or some default data
    //);

    const options = {
        //customBars: true, sharmouta
        //drawPoints: true,
        //  valueRange: [0.0, 100],
        connectSeparatedPoints: true,
        showRoller: true,
        ylabel: ylabel,
            legend: 'always',
        labelsSeparateLines: true,
        showRangeSelector: true,
        highlightCircleSize: 4,
        highlightSeriesOpts: {
            strokeWidth: 2,
            strokeBorderWidth: 1,
            highlightCircleSize: 5
            },


      //  labels: ['X', 'Series1', 'Series2'], // Adjust this for your series
        highlightCallback: function(event, x, points, row, seriesName) {
            console.log(`Hovered on: ${seriesName}, X: ${x}, Y: ${points}`);
        }
    };


  //  if (divid == "graphdiv1") {

        headers = `${xlabel},${ylabel}\n`;
    g = new Dygraph(document.getElementById(divid), headers, options);
    dygraphInstances[divid] = g;
    //}

   // else {

   //     headers = `${xlabel},${ylabel}\n`;
    //    dygraphInstance2 = new Dygraph(document.getElementById(divid), headers, options);
   // }

}

function updateDygraph(divid,data, startTime) {

    try {
        const g = this.dygraphInstances[divid];
        if (g) {
            g.updateOptions({
                file: data,
            
            });

            if (startTime) {

                // Get the latest timestamp from the data
                var lastRow = data.split("\n").slice(-2, -1)[0]; // Get the second to last item (last is empty)
                var lastTimestamp = lastRow.split(",")[0];

           
                var endDate = new Date(lastTimestamp);
                var startDate = new Date(startTime); 

                 g.updateOptions({
                    dateWindow: [startDate.getTime(), endDate.getTime()]
                });
            }
        }


    } catch (error) {    
        console.error("Failed to update dygraph:", error);    
    }
}

function removeDygraph(divid) {
    try {
        //const g = this.dygraphInstances[divid];
        const g = document.getElementById(divid)
        if (g) {
            g.destroy(); // Clean up the Dygraph instance
            delete this.dygraphInstances[divid]; // Remove from the dictionary
        }
    } catch (error) {
        console.error("Failed to remove dygraph:", error);
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

