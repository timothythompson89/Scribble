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
_Avoid_: node, widget; "control" only for the kind below

**Control**:
An element the user interacts with to change a value or trigger an action (a Button, CheckBox, Slider, TabControl); every control is an element, not every element is a control.
_Avoid_: widget, input (for the element)

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

**Attached setting**:
A setting on a child that only its parent reads (`Grid.row 1`, `DockPanel.dock Dock.Top`, `Canvas.left 20.0`): a static member of the parent's settings type that any element accepts, ignored under any other parent.
_Avoid_: attached property (the setting is what's attached, not the property)

**Value**:
What an element currently has for a property, together with where it came from (own, inline, style, theme, inherited, default).
_Avoid_: property (for the value), effective value

**Controlled**:
Said of a control whose value the view gives (`CheckBox.isChecked model.Done`): it shows exactly that and only reports changes, which take effect when the view gives the new value.
_Avoid_: bound, two-way

**Own value**:
The value a control keeps for itself when the view doesn't give one; it survives the view being rebuilt.
_Avoid_: uncontrolled, internal state (state is a component's)

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

**Extent**:
The full size of what a scroll viewer scrolls over; the viewport shows part of it. An items view's extent counts only the items it has measured.
_Avoid_: content size, scroll size

**Realized item**:
An item of an items view that currently has an element; items outside the viewport have none and keep no state.
_Avoid_: rendered item, materialized item

**Recycling**:
When an item scrolls out of an items view, its element is kept and given to an item scrolling in rather than made again; components inside it lose their state. To the pointer it is then another item: a pointer over it leaves the old item and enters the new one, and a press on it no longer clicks.
_Avoid_: pooling, reuse (for this)

**Scroll anchor**:
The item of an items view that keeps its place on screen while sizes or items above it change: the first visible item, found by key (the next one if it's removed).
_Avoid_: pinned item, sticky item

**Scroller**:
The controller an app uses to scroll a scroll viewer or items view as an action (`ctx.useScroller ()`).
_Avoid_: scroll state, scroll position (for the controller)

**Element tree**:
The elements realized from one root view, with their layout, focus and pending work.
_Avoid_: visual tree, DOM

**Layer**:
One element tree in a window's ordered stack (app, popups, modal, inspector), with its own focus; it lets the pointer through where it has nothing, or blocks everything below.
_Avoid_: popup, portal, overlay

**Flyout**:
Content shown in its own layer, anchored to an element (`Button.flyout menu`); it closes when you press elsewhere or press Escape. The Flyout is also the box its content sits in, and is styled as if inside its element.
_Avoid_: popup, dropdown (for the mechanism; a ComboBox's list is its flyout)

**Tooltip**:
A short hint shown near the pointer after it rests on an element; never focused, never clicked.

**Host**:
What runs a window's layers: input, frames and the window's theme (`ElementHost`).
_Avoid_: window (for the host), root

## Images

**Image**:
An immutable value naming a picture to show and how to read it (its source, its pixels per DIP); the same image is decoded once however many places show it.
_Avoid_: bitmap, image source (for the value)

**Decoded image**:
An image's pixels once read from its file (or given as pixels), ready to draw; an Image has one once it has loaded,
and element authors draw one.
_Avoid_: bitmap, texture, image pixels

**ImageView**:
The element that draws an image in its box.
_Avoid_: image control, picture box

**Density**:
How many of an image's pixels make one DIP: 1 by default, 2 for an image made for a 2× screen.
_Avoid_: DPI, scale (for an image's own density)

**Holding an image**:
An element holds the images it shows, keeping them decoded; it lets one go when it stops showing it or leaves its tree.
_Avoid_: referencing, owning, loading (for holding)

**Image pool**:
Recently shown images that nothing holds any more, kept decoded up to a size limit in case they are shown again.
_Avoid_: image cache (for the pool alone)

## Accessibility

**Semantics**:
What an element is to assistive technology: its role, name, value, states and actions, given as settings (`Semantics.role`, `Semantics.label`) by its element type's defaults or by a view.
_Avoid_: accessibility info, automation properties (for ours)

**Role**:
The kind of thing an element is to assistive technology (button, checkbox, slider, list item), from a fixed set based on ARIA's roles.
_Avoid_: control type, semantic type

**Semantic node**:
One element as assistive technology sees it; elements with nothing to say (layout panels, hidden or decorative ones) have none, and the text inside a button becomes its name rather than a node.
_Avoid_: accessibility node, automation peer

**Semantic tree**:
The semantic nodes of a window's layers, built from the element trees when asked for (never per frame).
_Avoid_: accessibility tree (for ours; that's the platform's)

**Semantic action**:
Something assistive technology can ask a semantic node to do (click, increment, decrement, focus, scroll into view); it takes the same path as the matching input.
_Avoid_: automation pattern, command
