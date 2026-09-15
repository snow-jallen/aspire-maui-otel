Feature: Fetching the weather

Scenario: The forecast list is filled from the API
	Given the API returns 3 forecasts
	When the user asks for the weather
	Then 3 forecasts are shown
	And the status mentions 3 forecasts

Scenario: A failing API is reported rather than crashing
	Given the API is unavailable
	When the user asks for the weather
	Then no forecasts are shown
	And the status mentions a failure
