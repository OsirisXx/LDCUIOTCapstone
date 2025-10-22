<!-- d80ea58f-36da-4aeb-a00e-e80784355efa 446e63b9-38f2-43db-b190-2f381c34bc43 -->
# Fix MIT App Inventor Meals/Drinks Value Display

## Problem

When users order from Meals/Drinks pages and press Accept, the text fields on Screen1 show "0" instead of the ordered amounts because Screen1.Initialize only runs when the app first opens, not when navigating back.

## Solution Overview

Modify the Meals/Drinks pages to close the screen instead of opening Screen1, and add a mechanism to load saved values when Screen1 becomes visible again.

## Changes Needed

### 1. Modify Meals Page Accept Button

**Current behavior:**

- Saves to TinyDB1 with tag "MealPrice"
- Opens Screen1 (which doesn't reload values)

**Change to:**

```
when btnAccept.Click do:
  if txtTotal.Text = "" or txtTotal.Text = "0" then:
    call Notifier1.ShowAlert notice "Please select your order"
  else:
    call TinyDB1.StoreValue tag "MealPrice" valueToStore txtTotal.Text
    close screen
```

**Key change:** Replace `open another screen screenName Screen1` with `close screen`

### 2. Modify Drinks Page Accept Button

**Current behavior:**

- Saves to TinyDB1 with tag "DrinkPrice"
- Opens Screen1 (which doesn't reload values)

**Change to:**

```
when btnAccept.Click do:
  if txtTotal.Text = "" or txtTotal.Text = "0" then:
    call Notifier1.ShowAlert notice "Please select your order"
  else:
    call TinyDB1.StoreValue tag "DrinkPrice" valueToStore txttotalfinal.Text
    close screen
```

**Key change:** Replace `open another screen screenName Screen1` with `close screen`

### 3. Modify Screen1 Navigation Buttons

**Update btnMeals.Click:**

```
when btnMeals.Click do:
  open another screen screenName Meals
```

**After returning (add new event handler):**

Since Screen1.Resume isn't available, we'll load values in the navigation buttons BEFORE opening screens:

**Actually, better approach - modify Screen1.Initialize to always load latest values:**

```
when Screen1.Initialize do:
  set global MyMeals to call TinyDB1.GetValue tag "salestotal" valueIfTagNotThere make a list
  set global MyDrinks to call TinyDB1.GetValue tag "salestotal" valueIfTagNotThere make a list
  set txtMeals.Text to call TinyDB1.GetValue tag "MealPrice" valueIfTagNotThere "0"
  set txtDrinks.Text to call TinyDB1.GetValue tag "DrinkPrice" valueIfTagNotThere "0"
  set ListView1.Elements to get global MyMeals
```

### 4. Clear Values Only After Successful Transaction

**In Screen1 btnAccept.Click, keep the clearing logic:**

```
when btnAccept.Click do:
  if (btnMeals.Text = "" or btnDrinks.Text = "") then:
    call Notifier1.ShowAlert notice "No Transaction"
  else:
    add items to list list get global MyMeals item join (Label1.Text, "-", txtTotal.Text)
    call TinyDB1.StoreValue tag "salestotal" valueToStore get global MyMeals
    set ListView1.Elements to get global MyMeals
    set txtMeals.Text to "0"
    set txtDrinks.Text to "0"
    set txtTotal.Text to "0"
    call TinyDB1.StoreValue tag "MealPrice" valueToStore "0"
    call TinyDB1.StoreValue tag "DrinkPrice" valueToStore "0"
```

**Add two StoreValue calls at the end to clear the saved values in the database.**

## Expected Flow After Changes

1. App opens → Screen1.Initialize loads saved values (or "0" if none)
2. User clicks "Meals" → Goes to Meals page
3. User orders Chicken (300) → Clicks Accept
4. Meals page saves "300" to TinyDB1 → Closes screen
5. Screen1 reopens → Screen1.Initialize runs again → Loads "300" into txtMeals.Text
6. User sees "300" in the Meals field ✅
7. User clicks Accept on Screen1 → Transaction saved → Fields cleared to "0" → Database values cleared

## Summary of Block Changes

1. **Meals page btnAccept:** Change `open another screen` → `close screen`
2. **Drinks page btnAccept:** Change `open another screen` → `close screen`
3. **Screen1.Initialize:** Load values from TinyDB1 instead of hardcoding "0"
4. **Screen1 btnAccept:** Add StoreValue calls to clear database values after transaction