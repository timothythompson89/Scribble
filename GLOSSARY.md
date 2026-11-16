# Scribble

An F# UI framework written from the GPU up: apps describe their UI as views; Scribble keeps a tree of
elements in step with them and draws it with WebGPU. These are the words we use for its parts (agreed
2026-11-16; use them in code comments, docs and discussion).

## Views and elements

**View**:
An immutable description of a piece of UI: an element type plus a list of settings, returned by view functions.
_Avoid_: virtual node, template, widget

**Element**:
The live object a view is realized into, with its own values, layout and input state, living in an element tree.
_Avoid_: control, node, widget

**Element type**:
The behaviour shared by every element of a kind (its measure, arrange, render and other stages, and per-type defaults), compared by identity.
_Avoid_: element class, control type

**Settings type**:
The class whose static members are an element type's settings (`Border`, `Button`); settings types inherit, so shared settings appear everywhere.
_Avoid_: marker type

**Component**:
A function from its props to a view, with its own state kept between runs (hooks), hosted by an element.
_Avoid_: custom control, widget

**Props**:
The one value a component receives from the view that uses it; only for components.
_Avoid_: prop (for a property), input, arguments

## Properties and values

**Property**:
A typed key an element type declares (`Prop<'T>`), with a default and what a change to it affects (measure, arrange, render).
_Avoid_: prop (alone), attribute, field

**Setting**:
A property paired with a value in a view's list (`Border.background c`); also any item of a settings list that configures something (`App.title`).
_Avoid_: attribute, modifier, option

**Value**:
What an element currently has for a property, together with where it came from (own, inline, style, theme, inherited, default).
_Avoid_: property (for the value), effective value

## Looks

**Style**:
A selector (element type, class, visual state, property test) with settings, applied to the elements in its scope.
_Avoid_: rule, class (for a style)

**Theme**:
A set of theme values plus the styles of the built-in looks, applying to everything inside the element or window it's set on.
_Avoid_: skin, palette (for the whole theme)

**Theme value**:
A named, typed value a theme supplies (`Palette.accent`), which settings can take in place of a fixed value and which is read from the nearest theme.
_Avoid_: token, theme token, resource

**App value**:
A named, typed value with one current value per app (`Theme.current`, `WindowTitle.current`), shared by all its windows and layers.
_Avoid_: global, app state

## State

**Visual state**:
A condition of an element that styles can match: hover, pressed, focused, disabled, or one an app registers (`States.hover`).
_Avoid_: state (alone), pseudo-class

**State**:
A component's own remembered data, kept between runs (`useState`).
_Avoid_: model (for component state)

**Model**:
The value an Elmish app's `update` produces and its view is made from.
_Avoid_: state (for the app's model), store

## Structure

**Viewport**:
The part of an element that the scroll viewers around it currently show, passed down from them during layout.
_Avoid_: visible area, clip rect (for the viewport)

**Realized item**:
An item of an items view that currently has an element; items outside the viewport have none and keep no state.
_Avoid_: rendered item, materialized item

**Scroller**:
The controller an app uses to scroll a scroll viewer or items view as an action (`ctx.useScroller ()`).
_Avoid_: scroll state, scroll position (for the controller)

**Element tree**:
The elements realized from one root view, with their layout, focus and pending work.
_Avoid_: visual tree, DOM

**Layer**:
One element tree in a window's ordered stack (app, popups, modal, inspector), with its own focus; it lets the pointer through where it has nothing, or blocks everything below.
_Avoid_: popup, portal, overlay

**Host**:
What runs a window's layers: input, frames and the window's theme (`ElementHost`).
_Avoid_: window (for the host), root
