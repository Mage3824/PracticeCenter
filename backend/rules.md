Information System Requirements

1.1 Business Requirements
A web platform for the centralized collection of work reports and time tracking, deployed on-premise on the customer's server.
Key problem:
Employees send text reports via various messengers, and the manager has to collect and consolidate the information manually.
Solution:
A single platform where employees:

    Manually mark the start and end of the working day

    Write text reports on the work performed

    Manually record breaks
    The manager receives:

    Centralized access to all reports

    Daily summaries by employee

    Monthly reports with the option to export to Excel

1.2 Business Rules
Time Tracking
BR1. An employee can start the working day only once per day. An attempt to press "Start work" again without first finishing the previous shift is blocked.
BR2. An employee can finish the working day only after it has started. The "Finish work" button is disabled if the working day has not started.
BR3. The system automatically finishes the working day at 23:59:59 if the employee has not finished it manually. An entry marked "Auto-finish" is created in the event log.
If an employee continues working after midnight, they must manually press "Start work" for the new calendar day. The system does not start working days automatically.
BR4. Working time is calculated as the difference between the end time and the start time minus the total duration of all breaks.
BR5. The minimum duration of a working day for report generation is 15 minutes. If less than 15 minutes are worked, the day is not counted in the monthly report.

Breaks
BR6. A break can only be started during an active working day (between "Start work" and "Finish work").
BR7. A break must be finished before finishing the working day. If the employee presses "Finish work" during an active break, the system automatically finishes the break before finishing the day.
BR8. The minimum duration of a break is 5 minutes. Breaks shorter than 5 minutes are not counted in working time calculation and are not saved in reports.
BR9. The maximum total duration of breaks per day is 4 hours. If the limit is exceeded, the system sends a notification to the manager.
BR10. An employee can have only one active break at a time. An attempt to start a new break while a previous one is active is blocked.
BR11. Break time is not counted in total working time. Formula: Working time = (End of day - Start of day) - Sum of breaks.

Reports
BR12. An employee can edit the text report until the end of the current working day. After that, editing is available only to the administrator.
BR13. A manager can add comments to employee reports. Comments are visible only to the manager and administrator.
BR14. An employee's daily report is generated automatically after the working day is finished and contains:

    Start and end time of work

    List of all breaks with start and end times

    Total working time (hours:minutes)
    BR15. The monthly report includes only completed working days. Incomplete days (where there is no end time) are not included in the report.
    BR16. The monthly report contains a table: Employee Full Name × days of the month × hours worked + a total column "Total hours for the month".
    BR17. Export of the monthly report to Excel is available only to the "Manager" and "Administrator" roles.
    BR18. The monthly report is generated for a calendar month (from the 1st to the last day). Export for a custom period is also available.

Roles and Access Rights
BR19. The system supports three roles:

    Administrator: full access to all functions, user management, system configuration

    Manager: viewing reports of all employees, adding comments, exporting reports, setting and editing absences

    Employee: managing their own working time, viewing and editing their own reports
    BR20. The system does not allow deactivating/deleting an administrator if this action results in 0 active administrators remaining in the system.
    BR21. An employee can see only their own reports. Attempting to access other employees' reports is blocked.
    BR22. A manager can see all employees' reports but cannot edit their working hours and breaks. They can only add comments.
    BR23. An administrator can edit any employee's data (start/end time, breaks, text reports) with mandatory logging of changes.
    BR23.1. The system provides a "Closing of the billing period" status (configured by the Administrator, for example, on the 5th of each month).
    BR23.2. The Administrator's interface must have a "Reopen closed period" function (with mandatory specification of the reason/act number), which programmatically removes the block, allows changes to be made, and automatically logs this action marked as "Emergency correction".
    BR24. When an employee is deactivated, their account is preserved, but access to the system is blocked. All historical data remains available for viewing.

Absences (sick leave, vacations)
BR25. The Administrator and Manager can manually set the "Absence" status for an employee for a specific day, specifying the reason (sick leave, vacation, day off, absence).
BR26. Days with the "Absence" status are not counted in the calculation of hours worked in the monthly report but are displayed as a separate column.
BR27. An employee cannot start the working day if the Administrator has set the "Absence" status for this day. The "Start work" button in the employee's interface becomes inactive (blocked). When hovering over it, a tooltip is displayed: "You have been set the status [Absence Reason] for this date. To start working, please contact your manager to remove the status."
BR28. The "Absence" status can be changed by the Administrator and Manager. Employees cannot change this status.

System and Data
BR29. All user actions are logged: who, when, what they did. Logs are stored for 1 year and are accessible only to the Administrator.
BR30. When data is modified (start/end time, breaks, reports), the system saves the previous value in the change history, indicating the author and time of the change.
BR31. The system requires confirmation for critical actions: finishing the working day, deleting a break, changing the start/end time after the day has finished.
BR32. A user's session expires after 8 hours of inactivity. After that, a re-login to the system is required. At the same time, background time tracking on the server is not interrupted.
BR33. The system automatically creates a database backup daily at 03:00. Backups are stored for 30 days.
BR34. User passwords are stored in hashed form. Storage of passwords in plain text is prohibited.
BR35. Upon attempting to log in with an incorrect password 5 times in a row, the account is locked for 15 minutes. The user must be notified upon the 2nd incorrect password and subsequently with a countdown to the lock. After 10 failed attempts per day, the user's account is blocked until the end of the day; only an administrator can unlock it early. (applies only to managers and administrators)
BR36. After 2 incorrect passwords, each subsequent attempt requires CAPTCHA input. (applies only to managers and administrators)