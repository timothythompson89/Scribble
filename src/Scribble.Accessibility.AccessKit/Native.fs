/// Hand-written AccessKit declarations, only what Scribble uses, checked against accesskit.h of accesskit-c 0.23.2
/// (build/AccessKit.targets pins the version). Rules as for SDL and wgpu: blittable parameters and fields only, C
/// `bool` as `byte`, enums (all `uint8_t`) as `byte`, strings as UTF-8 bytes with their length, so nothing needs
/// runtime marshalling and everything works under NativeAOT.
module internal Scribble.Accessibility.AccessKit.Native

#nowarn "9"

open System
open System.Runtime.InteropServices

[<Literal>]
let Lib = "accesskit"

/// `accesskit_rect`: left, top, right, bottom.
[<Struct; StructLayout(LayoutKind.Sequential)>]
type Rect =
    val mutable X0: float
    val mutable Y0: float
    val mutable X1: float
    val mutable Y1: float

/// `accesskit_activation_handler_callback` and `accesskit_tree_update_factory`: userdata in, a tree update out
/// (its ownership goes to AccessKit).
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type TreeUpdateCallback = delegate of nativeint -> nativeint

/// `accesskit_action_handler_callback`: the request (to be freed with `accesskit_action_request_free`), userdata.
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type ActionCallback = delegate of nativeint * nativeint -> unit

/// In `accesskit_action_request`: `action` (a byte) is first; `target_tree` (16 bytes) follows it, so
/// `target_node` (a uint64, aligned to 8) is at 24.
let ActionRequestActionOffset = 0
let ActionRequestTargetNodeOffset = 24
/// `data` (an `accesskit_opt_action_data`) follows at 32: `has_value` (a byte) there; its `value` (an
/// `accesskit_action_data`, aligned to 8) at 40, which is a tag (a C enum: 4 bytes) and, at 48, a union.
let ActionRequestHasDataOffset = 32
let ActionRequestDataTagOffset = 40
/// `ACCESSKIT_ACTION_DATA_VALUE`: the union holds a `char *` (UTF-8, ended by a zero).
let ActionDataValue = 1
/// `ACCESSKIT_ACTION_DATA_SET_TEXT_SELECTION`: the union holds an `accesskit_text_selection`: the anchor's node
/// (uint64) and character index (size_t), then the focus's.
let ActionDataSetTextSelection = 7
let ActionRequestDataOffset = 48

/// `accesskit_text_position` and `accesskit_text_selection`.
[<Struct; StructLayout(LayoutKind.Sequential)>]
type TextPosition =
    val mutable Node: uint64
    val mutable CharacterIndex: unativeint

[<Struct; StructLayout(LayoutKind.Sequential)>]
type TextSelection =
    val mutable Anchor: TextPosition
    val mutable Focus: TextPosition

// ---- Nodes ----

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint accesskit_node_new(byte role)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_label_with_length(nativeint node, byte* value, unativeint length)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_value_with_length(nativeint node, byte* value, unativeint length)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_bounds(nativeint node, Rect value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_add_action(nativeint node, byte action)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_children(nativeint node, unativeint length, uint64* values)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_toggled(nativeint node, byte value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_selected(nativeint node, byte value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_expanded(nativeint node, byte value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_disabled(nativeint node)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_read_only(nativeint node)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_multiselectable(nativeint node)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_modal(nativeint node)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_clips_children(nativeint node)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_numeric_value(nativeint node, float value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_min_numeric_value(nativeint node, float value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_max_numeric_value(nativeint node, float value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_numeric_value_step(nativeint node, float value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_orientation(nativeint node, byte value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_position_in_set(nativeint node, unativeint value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_size_of_set(nativeint node, unativeint value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_scroll_x(nativeint node, float value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_scroll_x_min(nativeint node, float value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_scroll_x_max(nativeint node, float value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_scroll_y(nativeint node, float value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_scroll_y_min(nativeint node, float value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_scroll_y_max(nativeint node, float value)

// ---- Text runs and a text box's selection ----

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_character_lengths(nativeint node, unativeint length, byte* values)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_word_starts(nativeint node, unativeint length, byte* values)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_character_positions(nativeint node, unativeint length, float32* values)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_character_widths(nativeint node, unativeint length, float32* values)

/// `accesskit_text_direction`: 0 is left to right.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_text_direction(nativeint node, byte value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_previous_on_line(nativeint node, uint64 value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_next_on_line(nativeint node, uint64 value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_node_set_text_selection(nativeint node, TextSelection value)

// ---- Tree updates ----

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint accesskit_tree_info_new(uint64 root)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_tree_info_set_toolkit_name_with_length(nativeint tree, byte* name, unativeint length)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint accesskit_tree_update_with_capacity_and_focus(unativeint capacity, uint64 focus)

/// The update takes the tree info.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_tree_update_set_tree_info(nativeint update, nativeint tree)

/// The update takes the node.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_tree_update_push_node(nativeint update, uint64 id, nativeint node)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_action_request_free(nativeint request)

// ---- Windows: the adapter that subclasses a window it doesn't own ----

/// Before the window is first shown, on the window's thread (it panics on a visible window). The activation
/// handler is always called on that thread; the action handler on any.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint accesskit_windows_subclassing_adapter_new(
    nativeint hwnd,
    nativeint activationHandler,
    nativeint activationUserdata,
    nativeint actionHandler,
    nativeint actionUserdata
)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_windows_subclassing_adapter_free(nativeint adapter)

/// Calls the factory only if something has asked for the tree. Null, or events to raise with
/// `accesskit_windows_queued_events_raise`.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint accesskit_windows_subclassing_adapter_update_if_active(nativeint adapter, nativeint updateFactory, nativeint userdata)

/// Raises the events and frees them.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void accesskit_windows_queued_events_raise(nativeint events)
