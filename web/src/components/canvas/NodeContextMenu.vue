<template>
  <Teleport to="body">
    <div
      v-if="visible"
      class="fixed z-50 bg-white border border-gray-200 rounded-lg shadow-lg py-1 min-w-52"
      :style="{ top: `${y}px`, left: `${x}px` }"
      @click.stop
    >
      <template
        v-for="item in menuItems"
        :key="item.id"
      >
        <div
          v-if="'separator' in item"
          class="border-t border-gray-100 my-1"
        />
        <button
          v-else
          class="w-full flex items-center gap-3 px-4 py-2 text-sm transition-colors"
          :class="item.destructive ? 'text-red-600 hover:bg-red-50' : 'text-gray-700 hover:bg-gray-50'"
          @click="item.action"
        >
          <component
            :is="item.icon"
            class="w-4 h-4 flex-shrink-0"
            :class="item.destructive ? 'text-red-400' : 'text-gray-400'"
          />
          <span>{{ item.label }}</span>
        </button>
      </template>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import type { Component } from 'vue';
import {
  Edit2, Copy, PlusCircle, HelpCircle, Trash2,
  Zap, Variable, FlaskConical, Settings, ArrowRight, ArrowLeftRight, RotateCcw,
  Users, Clock, Eye, Bell, Filter, Bookmark,
  Brain, FileEdit, Flag, AlignLeft, Palette, Plus,
} from 'lucide-vue-next';

type ActionItem = {
  id: string;
  label: string;
  icon: Component;
  action: () => void;
  destructive?: true;
};
type SeparatorItem = { separator: true; id: string };
type MenuItem = ActionItem | SeparatorItem;

const props = defineProps<{ visible: boolean; x: number; y: number; nodeType?: string }>();
const emit = defineEmits<{
  edit: [];
  duplicate: [];
  'add-connected': [];
  help: [nodeType: string];
  delete: [];
  configure: [action: string];
  'add-branch': [];
  test: [nodeType: string];
  'preview-gate': [];
  'edit-conditions': [];
  'set-default-branch': [];
}>();

function getTypeSpecificItems(nodeType: string | undefined): MenuItem[] {
  switch (nodeType) {
    case 'trigger':
      return [
        { id: 'sep-type', separator: true },
        { id: 'configure-trigger', label: 'Configure trigger', icon: Zap, action: () => emit('configure', 'trigger') },
        { id: 'set-variables', label: 'Set input variables', icon: Variable, action: () => emit('configure', 'variables') },
        { id: 'test-trigger', label: 'Test trigger', icon: FlaskConical, action: () => emit('test', 'trigger') },
      ];
    case 'action':
      return [
        { id: 'sep-type', separator: true },
        { id: 'configure-action', label: 'Configure action', icon: Settings, action: () => emit('configure', 'action') },
        { id: 'map-inputs', label: 'Map inputs', icon: ArrowRight, action: () => emit('configure', 'inputs') },
        { id: 'map-outputs', label: 'Map outputs', icon: ArrowLeftRight, action: () => emit('configure', 'outputs') },
        { id: 'set-retry', label: 'Set retry policy', icon: RotateCcw, action: () => emit('configure', 'retry') },
      ];
    case 'human-gate':
      return [
        { id: 'sep-type', separator: true },
        { id: 'configure-gate', label: 'Configure gate', icon: Users, action: () => emit('configure', 'gate') },
        { id: 'set-sla', label: 'Set SLA timeout', icon: Clock, action: () => emit('configure', 'sla') },
        { id: 'preview-gate', label: 'Preview gate portal', icon: Eye, action: () => emit('preview-gate') },
        { id: 'configure-notifications', label: 'Configure notifications', icon: Bell, action: () => emit('configure', 'notifications') },
      ];
    case 'router':
      return [
        { id: 'sep-type', separator: true },
        { id: 'add-branch', label: 'Add output branch', icon: Plus, action: () => emit('add-branch') },
        { id: 'edit-conditions', label: 'Edit conditions', icon: Filter, action: () => emit('edit-conditions') },
        { id: 'set-default-branch', label: 'Set default branch', icon: Bookmark, action: () => emit('set-default-branch') },
      ];
    case 'ai':
      return [
        { id: 'sep-type', separator: true },
        { id: 'configure-model', label: 'Configure model', icon: Brain, action: () => emit('configure', 'model') },
        { id: 'edit-prompt', label: 'Edit prompt', icon: FileEdit, action: () => emit('configure', 'prompt') },
        { id: 'map-inputs-outputs', label: 'Map inputs / outputs', icon: ArrowLeftRight, action: () => emit('configure', 'io') },
        { id: 'test-node', label: 'Test node', icon: FlaskConical, action: () => emit('test', 'ai') },
      ];
    case 'end':
      return [
        { id: 'sep-type', separator: true },
        { id: 'configure-outcome', label: 'Configure outcome', icon: Flag, action: () => emit('configure', 'outcome') },
        { id: 'add-notification', label: 'Add notification', icon: Bell, action: () => emit('configure', 'end-notification') },
      ];
    case 'note':
      return [
        { id: 'sep-type', separator: true },
        { id: 'edit-text', label: 'Edit text', icon: AlignLeft, action: () => emit('configure', 'note-text') },
        { id: 'change-colour', label: 'Change colour', icon: Palette, action: () => emit('configure', 'note-colour') },
      ];
    default:
      return [];
  }
}

const menuItems = computed((): MenuItem[] => {
  const nodeType = props.nodeType;
  const baseItems: MenuItem[] = [
    { id: 'edit', label: 'Edit', icon: Edit2, action: () => emit('edit') },
    { id: 'duplicate', label: 'Duplicate', icon: Copy, action: () => emit('duplicate') },
    { id: 'add-connected', label: 'Add connected node', icon: PlusCircle, action: () => emit('add-connected') },
    { id: 'help', label: `Help with ${nodeType ?? 'this'} node`, icon: HelpCircle, action: () => emit('help', nodeType ?? '') },
  ];
  const typeItems = getTypeSpecificItems(nodeType);
  const destructiveItems: MenuItem[] = [
    { id: 'sep-delete', separator: true },
    { id: 'delete', label: 'Delete', icon: Trash2, action: () => emit('delete'), destructive: true },
  ];
  return [...baseItems, ...typeItems, ...destructiveItems];
});
</script>
