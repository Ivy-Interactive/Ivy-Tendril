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

interface VerificationItem {
  name: string;
  enabled: boolean;
  required: boolean;
}

interface SortableVerificationListProps {
  itemsJson?: string;
  onReorder?: (newIndices: number[]) => void;
  onChange?: (item: VerificationItem) => void;
  id?: string;
}

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
  itemsJson = "[]",
  onReorder,
  onChange,
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

        onReorder?.(reorderMapping);
      }
    }
  };

  const handleChange = (updatedItem: VerificationItem) => {
    setItems((prev) => prev.map((item) => (item.name === updatedItem.name ? updatedItem : item)));
    onChange?.(updatedItem);
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
