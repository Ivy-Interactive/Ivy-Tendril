import React from "react";
import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { GripVertical } from "lucide-react";
import "./sortable-verification-list.css";

type IvyEventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

interface VerificationItem {
  name: string;
  enabled: boolean;
  required: boolean;
}

interface SortableVerificationListProps {
  id: string;
  itemsJson?: string;
  events?: string[];
  eventHandler?: IvyEventHandler;
}

const EMPTY_EVENTS: string[] = [];

function SortableItem({
  item,
  onChange,
}: {
  item: VerificationItem;
  onChange?: (item: VerificationItem) => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: item.name,
  });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  return (
    <div ref={setNodeRef} style={style} className="svl-row">
      <button {...attributes} {...listeners} className="svl-handle" aria-label="Drag handle">
        <GripVertical className="svl-handle-icon" />
      </button>

      <label className="svl-label">
        <input
          type="checkbox"
          checked={item.enabled}
          onChange={(e) => onChange?.({ ...item, enabled: e.target.checked })}
          className="svl-checkbox"
        />
        <span className="svl-name">{item.name}</span>
      </label>

      <div className="svl-spacer"></div>

      {item.enabled && (
        <label className="svl-required">
          <input
            type="checkbox"
            checked={item.required}
            onChange={(e) => onChange?.({ ...item, required: e.target.checked })}
            className="svl-checkbox"
          />
          <span>Required</span>
        </label>
      )}
    </div>
  );
}

export function SortableVerificationList({
  id,
  itemsJson = "[]",
  events = EMPTY_EVENTS,
  eventHandler,
}: SortableVerificationListProps) {
  const [items, setItems] = React.useState<VerificationItem[]>(() => {
    try {
      return JSON.parse(itemsJson);
    } catch {
      return [];
    }
  });

  React.useEffect(() => {
    try {
      const parsed = JSON.parse(itemsJson);
      setItems(parsed);
    } catch {
      setItems([]);
    }
  }, [itemsJson]);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 3 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;

    if (over && active.id !== over.id) {
      const oldIndex = items.findIndex((item) => item.name === active.id);
      const newIndex = items.findIndex((item) => item.name === over.id);

      if (oldIndex !== -1 && newIndex !== -1) {
        const newOrder = arrayMove(items, oldIndex, newIndex);
        setItems(newOrder);

        // Create index mapping: new position -> original position
        const originalItems = JSON.parse(itemsJson) as VerificationItem[];
        const reorderMapping = newOrder.map((item) =>
          originalItems.findIndex((orig) => orig.name === item.name),
        );

        // The C# OnReorder event carries a single string value (JSON array of indices).
        if (eventHandler && events.includes("OnReorder")) {
          eventHandler("OnReorder", id, [JSON.stringify(reorderMapping)]);
        }
      }
    }
  };

  const handleChange = (updatedItem: VerificationItem) => {
    setItems((prev) => prev.map((item) => (item.name === updatedItem.name ? updatedItem : item)));
    // The C# OnChange event carries a single string value (JSON of the updated item).
    if (eventHandler && events.includes("OnChange")) {
      eventHandler("OnChange", id, [JSON.stringify(updatedItem)]);
    }
  };

  return (
    <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
      <SortableContext items={items.map((item) => item.name)} strategy={verticalListSortingStrategy}>
        <div className="svl-root">
          {items.map((item) => (
            <SortableItem key={item.name} item={item} onChange={handleChange} />
          ))}
        </div>
      </SortableContext>
    </DndContext>
  );
}
