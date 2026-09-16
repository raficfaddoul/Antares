# Collecting server API for project Antares

This specification document describe all API accessible by external programs
to retrieve sensors data and control the on-site alarm.

## Sensors data collecting and storage

Sensors data are collected by the collector service every minute.
For each run, the collector service query every sensor asynchronously at the same time.
When sensors response are received, all data are encapsulated into a dictionary called a "report".
One report contain all data from all sensors, timestamped with the time when the collector started running.

## Sensors data gathering endpoint (read only)

This endpoint send back all data reports starting from the given datetime (excluded) up to current datetime.

This endpoint need to be called regularly (every minute) by the computing application to get all reports as soon as
possible.
Each API call MUST include a start date equal to the latest received report to retrieve only new reports in an
incremental manner.

This endpoint is stateless. Returned reports are not modified in any way (no side effects) and the given start date is
not recorded.
If required, all historic data can be requested by giving a start date way in the past (using 1/1/1970 UNIX epoch for
example).

> Allowed methods: GET
>
> Ressource URI: /api/v1/reports
>
> Request parameters:
> - "from" (integer, required): UNIX timestamp (exclusive)
>
> Response mimetype : application/json
>
> Return codes:
> - 200 "OK" with reports as json body (see format below)
> - 204 "No Content" without body if no new report to send back
> - 400 "Bad Request" if "from" parameter format not valid

Remarks:
If more than 1000 reports to send back ("from" parameter way in the past), only the first 1000 reports will be sent.
In such case, client need to send a new request with the "from" parameter equal to the latest report datetime to get the
following 1000 reports (at most).
To get all data up to the current time, client should keep requested new data until no more reports are returned by the
endpoint.

## Alarm management endpoint (write only)

This endpoint allow an external application to trigger or reset the on-site alarm.

The on-site alarm has both a light source and a siren.

Possibles alarm hardware states:

- OFF (no power): No light, No siren
- Normal (idle state): Green solid light, No Siren
- Danger (immediate action required): Red flashing light, Siren ringing

> Allowed methods : POST
>
> Ressource URI: /api/v1/alarm
>
> Request parameters:
> - "state" (string, required): one of the following values "normal", "danger"
>
> Return codes:
> - 200 "Ok" without body on success
> - 400 "Bad Request" if "state" parameter format not valid
> - 503 "Service Unavailable" if on-site automation controller is not responding to commands

Remarks:
The "danger" state MUST be cleared with "normal" state once the problem is resolved.
On-site users does not have any way to clear the "danger" state (unless powering off the whole alarm).

The on-site controller and collecting server implement a keep-alive system to detect hardware failure.
For this reason, updating the alarm state to "normal" is **asynchronous**.
200 "Ok" will be returned before the actual command is sent to the on-site controller.

However, for security reasons, updating the alarm state to "danger" is **synchronous**.
If the collecting server cannot contact the on-site controller, an HTTP error will be returned.
Because of network latency, setting the state to "danger" can take some time (a second or so).
In case of an HTTP error, retry mechanism must be implemented by the calling application.

## JSON reports data format

All reports returned in HTTP body will use the mimetype "application/json" with the following format (c-style
comments) :

```
[ // Root object is an array of reports
    { // Each report is a dictionary
    
        // Report format versionning
        "report_type": "antares", // Report type is fixed to "antares"
        "report_version": 1, // Report version is fixed to 1

        // Report metadata
        "report_datetime": 1731577300, // Report creation datetime as UNIX timestamp
        
        // Report data
        "report_data":
        [ // List of sensors data (one entry per sensor)
            { // Each sensor data entry is a dictionary
                "name": "NAME", // Sensor name (F1, F2, Fx, Ix, Hx, ...)
                "type": "TYPE", // Data type, possible values are "inclinometer", "potentiometer", "hygrometer", "weight_scale", "strain_gauge", "wind_station", "snow_scale"
                "values": {
                    // See below for content
                },
                "error" : "" // In case of error values will be replaced with "error" and the attached error message as string
            },
            // Next sensor data goes here and so on
        ]
    },
    // Next report goes here and so on
]
```

Possibles values structure for each data type :

Type == "inclinometer"

```
{
    "temperature": 0.000, // Celsius
    "x_axis": 0.000, // Degrees
    "y_axis": 0.000, // Degrees
}
```

Type == "potentiometer"

```
{
    "temperature": 0.000, // Celsius
    "extension": 0.000, // Millimeters
}
```

Type == "hygrometer"

```
{
    "external_temperature": 0.000, // Celsius, Outside enclosure
    "internal_temperature": 0.000, // Celsius, Inside enclosure
    "humidity": 0.000, // %RH
}
```

Type == "weight_scale"

```
{
    "temperature": 0.000, // Celsius
    "weight": 0.000, // Kilograms
}
```

Type == "strain_gauge"

```
{
    "temperature": 0.000, // Celsius
    "strain": 0.000, // Micro epsilon
}
```

Type == "wind_station"

```
{
    "speed": 0.000, // m/s
    "direction": 0.000, // degrees
}
```

Type == "snow_scale"

```
{
    "temperature": 0.000, // Celsius
    "weight": 0.000, // Kilograms
}
```
