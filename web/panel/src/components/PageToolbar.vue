<!-- 页面工具栏（筛选器、标签切换等）：放在标题下方的固定区域，不随内容滚动。 -->
<template>
  <Teleport to="#page-toolbar" defer>
    <div class="mb-4 flex flex-wrap items-center gap-3">
      <slot />
    </div>
  </Teleport>
</template>
