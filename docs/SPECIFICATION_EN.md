# Application Specification

# Neftyanik Gardening Association Portal

## Purpose

A Razor Pages application for a gardening association.

The system provides:

- user and role management;
- plot and ownership management;
- association and member electricity accounting;
- charges, payments, and allocations;
- expenses and financial reporting;
- news, documents, and system settings.

## User roles

### Member

- view owned plots;
- view charges, payments, and balances;
- view assigned electricity meters;
- submit readings for active member meters.

### Accountant

- manage readings, charges, payments, and expenses;
- manage association supplier tariffs;
- manage member electricity tariffs.

### Administrator

- manage users, roles, plots, ownership, and global settings.

## Electricity architecture

### Association electricity

- `AssociationElectricityReading` stores day/night readings for the shared association meter;
- `AssociationElectricityTariff` stores day/night supplier tariffs;
- `AssociationElectricityService` resolves tariffs only from `AssociationElectricityTariff`;
- shared electricity produces `Expense` records.

### Member electricity

- `MemberElectricityMeter` is a single-rate member meter with one billing plot;
- `MemberElectricityMeterPlot` links one meter to one or more member-owned plots;
- `MemberElectricityReading` stores reading history and calculated amounts;
- `MemberElectricityTariff` stores the single-rate tariff history;
- approved member readings produce `Charge` records.



