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
    <div
      ref={setNodeRef}
      style={style}
      className="flex items-center gap-2 py-2 px-3 bg-white border border-gray-200 rounded hover:bg-gray-50"
    >
      <button
        {...attributes}
        {...listeners}
        className="cursor-grab active:cursor-grabbing touch-none"
        aria-label="Drag handle"
      >
        <GripVertical className="w-5 h-5 text-gray-400" />
      </button>

      <label className="flex items-center gap-2 cursor-pointer">
        <input
          type="checkbox"
          checked={item.enabled}
          onChange={(e) => onChange?.({ ...item, enabled: e.target.checked })}
          className="w-4 h-4"
        />
        <span className="flex-1">{item.name}</span>
      </label>

      <div className="flex-1"></div>

      {item.enabled && (
        <label className="flex items-center gap-2 cursor-pointer text-sm">
          <input
            type="checkbox"
            checked={item.required}
            onChange={(e) => onChange?.({ ...item, required: e.target.checked })}
            className="w-4 h-4"
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
        <div className="flex flex-col gap-2">
          {items.map((item) => (
            <SortableItem key={item.name} item={item} onChange={handleChange} />
          ))}
        </div>
      </SortableContext>
    </DndContext>
  );
}
