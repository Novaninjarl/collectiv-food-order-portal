<script setup lang="ts">
import type { EventLog } from '../api';

defineProps<{
  events: EventLog[];
}>();
</script>

<template>
  <section class="card">
    <div class="card-header">
      <div>
        <p class="eyebrow">Event log</p>
        <h2>Operational audit trail</h2>
      </div>
      <span class="badge">{{ events.length }} events</span>
    </div>

    <div class="event-list">
      <article v-for="event in events" :key="event.id" class="event-item">
        <span class="event-type">{{ event.type }}</span>
        <p>{{ event.message }}</p>
      </article>
    </div>
  </section>
</template>
