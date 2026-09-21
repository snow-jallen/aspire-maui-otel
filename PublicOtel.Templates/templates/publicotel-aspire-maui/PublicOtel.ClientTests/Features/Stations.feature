Feature: Live station readings
	A reading reported on one device reaches every other device without anyone pressing refresh.

Scenario: A reported reading reaches the server
	Given the app is connected to the station hub
	When the user reports 24 degrees for station "north"
	Then the API receives a reading of 24 for station "north"

Scenario: A broadcast reading appears without a refresh
	Given the app is connected to the station hub
	When the server broadcasts 31 degrees for station "north"
	Then the latest reading shown is 31
	And the update list holds 1 entry

Scenario: The newest broadcast is shown first
	Given the app is connected to the station hub
	When the server broadcasts 10 degrees for station "north"
	And the server broadcasts 28 degrees for station "north"
	Then the latest reading shown is 28
	And the update list holds 2 entries
