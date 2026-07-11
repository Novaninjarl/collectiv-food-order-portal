<script setup lang="ts">
import type { RejectedOrder } from '../api';

withDefaults(defineProps<{
  rejectedOrders: RejectedOrder[];
  title?: string;
  eyebrow?: string;
}>(), {
  title: 'Clear validation failures',
  eyebrow: 'Rejected orders'
});
</script>

<template>
  <section class="card">
    <div class="card-header">
      <div>
        <p class="eyebrow">{{ eyebrow }}</p>
        <h2>{{ title }}</h2>
      </div>
      <span class="badge warning">{{ rejectedOrders.length }} rejected</span>
    </div>

    <p v-if="rejectedOrders.length === 0" class="hint">No rejected orders to show.</p>

    <div v-else class="rejected-list">
      <article v-for="order in rejectedOrders" :key="order.id" class="rejected-item">
        <strong>Order {{ order.sourceOrderId ?? 'new' }} · {{ order.customerName || 'Unknown customer' }}</strong>
        <ul>
          <li v-for="reason in order.reasons" :key="reason">{{ reason }}</li>
        </ul>
      </article>
    </div>
  </section>
</template>
