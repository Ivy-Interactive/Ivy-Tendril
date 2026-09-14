import { WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export interface FieldProps extends WidgetBaseProps {
    label?: string;
    description?: string;
    required?: boolean;
    /** Rendered as a tooltip on a question-mark icon beside the label. */
    help?: string;
    /** Buttons or links shown at the right of the label row. */
    tools?: React.ReactNode;
    labelPosition?: "Top" | "Left";
    children?: React.ReactNode;
}
/**
 * Label, description and help around any input. Mirrors `Ivy.Field`.
 *
 * @tags label wrapper form
 * @example <Field label="Email" required><TextInput value={email} /></Field>
 */
export declare const Field: ({ id, label, description, required, help, tools, labelPosition, density, width, height, aspectRatio, visible, children, className, style, }: FieldProps) => React.JSX.Element;
export interface FormProps extends WidgetBaseProps {
    children?: React.ReactNode;
    onSubmit?: (event: React.FormEvent<HTMLFormElement>) => void;
}
/**
 * Groups fields and captures submit. Mirrors `Ivy.Form`.
 *
 * @tags form submit group
 * @example <Form onSubmit={save}><Field label="Name"><TextInput /></Field></Form>
 */
export declare const Form: ({ id, children, width, height, aspectRatio, visible, className, style, onSubmit, }: FormProps) => React.JSX.Element;
