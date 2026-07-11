<script setup lang="ts">
import type { Customer } from '../api';

defineProps<{
  customers: Customer[];
}>();
</script>

<template>
  <section class="card">
    <div class="card-header">
      <div>
        <p class="eyebrow">Customers</p>
        <h2>Saved customer history</h2>
      </div>
      <span class="badge">{{ customers.length }} customers</span>
    </div>

    <div class="mini-list">
      <div v-for="customer in customers" :key="customer.id" class="mini-row customer-row">
        <div>
          <strong>{{ customer.name }}</strong>
          <small>{{ customer.city }} · {{ customer.postcode }}</small>
        </div>
        <span>{{ customer.orderCount }} orders</span>
        <span>{{ customer.lastOrderDate ?? 'No orders' }}</span>
      </div>
    </div>
  </section>
</template>
